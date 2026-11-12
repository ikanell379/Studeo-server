using System.Text.Json;
using System.Threading.Channels;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Studeo.Api.Auth;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Api.Controllers;

[ApiController]
[Authorize(Roles = Roles.Any)]
[Route("api/study-sets")]
public class StudySetsController(
    IStudySetService studySetService,
    IValidator<CreateStudySetRequest> createValidator,
    ILogger<StudySetsController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Generates study material for a section with the AI (can take a minute), validates and stores it.</summary>
    [HttpPost]
    [ProducesResponseType<StudySetResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<StudySetResponse>> Create(CreateStudySetRequest request, CancellationToken ct)
    {
        var validation = await createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var first = validation.Errors[0];
            return Error(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, first.ErrorMessage);
        }

        var result = await studySetService.CreateAsync(request, null, ct);
        if (result.ErrorCode is not null)
        {
            return Error(StatusFor(result.ErrorCode), result.ErrorCode, null);
        }

        return CreatedAtAction(nameof(Get), new { id = result.StudySet!.Id }, result.StudySet);
    }

    /// <summary>
    /// Same as POST /api/study-sets, but answers at once with a stream of JSON lines (application/x-ndjson):
    /// {"type":"progress","progress":{...}} while the AI writes (real progress: stage and finished items),
    /// then {"type":"done","studySet":{...}} or {"type":"error","code":"..."}.
    /// An invalid request still gets a plain 400.
    /// </summary>
    [HttpPost("stream")]
    [Produces("application/x-ndjson")]
    [ProducesResponseType<GenerationEvent>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task CreateWithProgress(CreateStudySetRequest request, CancellationToken ct)
    {
        var validation = await createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            await Error(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, validation.Errors[0].ErrorMessage)
                .ExecuteResultAsync(ControllerContext);
            return;
        }

        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        // Progress is reported from inside the generation; a channel hands it to this loop, which writes it out.
        var events = Channel.CreateUnbounded<GenerationEvent>(new UnboundedChannelOptions { SingleReader = true });
        var progress = new ChannelProgress(events.Writer);

        async Task GenerateAsync()
        {
            try
            {
                var result = await studySetService.CreateAsync(request, progress, ct);
                events.Writer.TryWrite(result.ErrorCode is null
                    ? GenerationEvent.Done(result.StudySet!)
                    : GenerationEvent.Error(result.ErrorCode));
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                logger.LogError(e, "Study set generation failed");
                events.Writer.TryWrite(GenerationEvent.Error("server_error"));
            }
            finally
            {
                events.Writer.TryComplete();
            }
        }

        var generation = GenerateAsync();
        await foreach (var e in events.Reader.ReadAllAsync(ct))
        {
            await Response.WriteAsync(JsonSerializer.Serialize(e, Json) + "\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        await generation;
    }

    /// <summary>Forwards progress to the response stream; drops repeats so only real changes are sent.</summary>
    private sealed class ChannelProgress(ChannelWriter<GenerationEvent> writer) : IProgress<GenerationProgress>
    {
        private GenerationProgress? _last;

        public void Report(GenerationProgress value)
        {
            if (value == _last) return;
            _last = value;
            writer.TryWrite(GenerationEvent.Of(value));
        }
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<StudySetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StudySetResponse>> Get(Guid id, CancellationToken ct)
    {
        var studySet = await studySetService.GetAsync(id, ct);
        return studySet is null
            ? Error(StatusCodes.Status404NotFound, ErrorCodes.NotFound, null)
            : Ok(studySet);
    }

    private static int StatusFor(string code) => code switch
    {
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.AiInvalidOutput or ErrorCodes.AiRefused => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status503ServiceUnavailable, // ai_not_configured, ai_failed
    };

    private ObjectResult Error(int status, string code, string? detail)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title: code, detail: detail);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
