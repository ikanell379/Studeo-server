using System.Text.Json;
using Studeo.Api.Contracts;
using Studeo.Api.Repositories;

namespace Studeo.Api.Services.Export;

/// <summary>A file to download.</summary>
public record ExportedFile(byte[] Content, string ContentType, string FileName);

public interface ITestExportService
{
    /// <summary>The owner's study set as a test (or answer key) in PDF or Word; null if they have no such set.</summary>
    Task<ExportedFile?> ExportAsync(Guid id, string ownerId, ExportTestRequest request, CancellationToken ct);
}

public class TestExportService(IStudySetRepository studySets) : ITestExportService
{
    public const string PdfType = "application/pdf";
    public const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public async Task<ExportedFile?> ExportAsync(Guid id, string ownerId, ExportTestRequest request, CancellationToken ct)
    {
        var entity = await studySets.GetAsync(id, ownerId, ct);
        if (entity is null)
        {
            return null;
        }

        var content = JsonSerializer.Deserialize<StudySet>(entity.Content, StudySetService.Json)!;
        var answerKey = request.Variant == ExportVariants.Answers;
        var title = string.IsNullOrWhiteSpace(request.Title) ? entity.Name : request.Title.Trim();
        var sheet = TestSheetBuilder.Build(content, title, answerKey, request.ShowPages, request.Types);

        return request.Format == ExportFormats.Docx
            ? new ExportedFile(DocxTestRenderer.Render(sheet), DocxType, TestSheetBuilder.FileName(title, answerKey, "docx"))
            : new ExportedFile(PdfTestRenderer.Render(sheet), PdfType, TestSheetBuilder.FileName(title, answerKey, "pdf"));
    }
}
