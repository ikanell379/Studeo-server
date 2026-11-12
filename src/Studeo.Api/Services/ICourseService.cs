using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

public interface ICourseService
{
    Task<IReadOnlyList<CourseResponse>> ListAsync(string ownerId, CancellationToken ct);

    /// <summary>A new course, or ErrorCode course_exists when the owner already has one with this name.</summary>
    Task<CourseResult> CreateAsync(string ownerId, string name, CancellationToken ct);

    /// <summary>ErrorCode not_found or course_exists on failure.</summary>
    Task<CourseResult> RenameAsync(Guid id, string ownerId, string name, CancellationToken ct);

    /// <summary>Deletes the course for good with all its PDFs and their quizzes. False if the owner has no such course.</summary>
    Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct);
}

/// <summary>Either Course (success) or ErrorCode.</summary>
public record CourseResult(CourseResponse? Course, string? ErrorCode);
