using Studeo.Api.Contracts;
using Studeo.Api.Models;
using Studeo.Api.Repositories;

namespace Studeo.Api.Services;

public class CourseService(ICourseRepository repository, TimeProvider timeProvider) : ICourseService
{
    public Task<IReadOnlyList<CourseResponse>> ListAsync(string ownerId, CancellationToken ct) =>
        repository.ListAsync(ownerId, ct);

    public async Task<CourseResult> CreateAsync(string ownerId, string name, CancellationToken ct)
    {
        name = name.Trim();
        if (await repository.NameTakenAsync(ownerId, name, null, ct))
        {
            return new CourseResult(null, ErrorCodes.CourseExists);
        }

        var course = new Course { Id = Guid.NewGuid(), OwnerId = ownerId, Name = name, CreatedAt = timeProvider.GetUtcNow() };
        await repository.AddAsync(course, ct);
        return new CourseResult(new CourseResponse(course.Id, course.Name, course.CreatedAt, 0), null);
    }

    public async Task<CourseResult> RenameAsync(Guid id, string ownerId, string name, CancellationToken ct)
    {
        name = name.Trim();
        if (await repository.GetAsync(id, ownerId, ct) is null)
        {
            return new CourseResult(null, ErrorCodes.NotFound);
        }
        if (await repository.NameTakenAsync(ownerId, name, id, ct))
        {
            return new CourseResult(null, ErrorCodes.CourseExists);
        }

        var course = (await repository.RenameAsync(id, ownerId, name, ct))!;
        var count = await repository.CountDocumentsAsync(id, ct);
        return new CourseResult(new CourseResponse(course.Id, course.Name, course.CreatedAt, count), null);
    }

    public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) => repository.DeleteAsync(id, ownerId, ct);
}
