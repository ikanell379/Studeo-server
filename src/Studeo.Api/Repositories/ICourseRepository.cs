using Studeo.Api.Contracts;
using Studeo.Api.Models;

namespace Studeo.Api.Repositories;

public interface ICourseRepository
{
    Task AddAsync(Course course, CancellationToken ct);

    /// <summary>The owner's courses by name, with how many PDFs each holds.</summary>
    Task<IReadOnlyList<CourseResponse>> ListAsync(string ownerId, CancellationToken ct);

    /// <summary>The owner's course, or null (also when it belongs to someone else).</summary>
    Task<Course?> GetAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>Whether the owner has another course with this name (ignoring case).</summary>
    Task<bool> NameTakenAsync(string ownerId, string name, Guid? exceptId, CancellationToken ct);

    Task<Course?> RenameAsync(Guid id, string ownerId, string name, CancellationToken ct);

    /// <summary>Deletes the owner's course with all its PDFs and their quizzes. False if there was none.</summary>
    Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct);

    Task<int> CountDocumentsAsync(Guid id, CancellationToken ct);
}
