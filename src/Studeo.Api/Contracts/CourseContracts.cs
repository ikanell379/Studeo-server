namespace Studeo.Api.Contracts;

/// <summary>A course of the signed-in user's library, with how many PDFs it holds. Listed by name.</summary>
public record CourseResponse(Guid Id, string Name, DateTimeOffset CreatedAt, int DocumentCount);

/// <summary>POST /api/courses (new course) and PATCH /api/courses/{id} (rename).</summary>
public record CourseNameRequest(string Name);

/// <summary>PUT /api/documents/{id}/course: file the PDF (with its quizzes) under a course; null takes it out.</summary>
public record MoveDocumentRequest(Guid? CourseId);

public static class CourseLimits
{
    public const int MaxNameLength = 100;
}
