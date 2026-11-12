namespace Studeo.Api.Contracts;

/// <summary>A stored PDF; CourseId is the course it is filed under (null = none).</summary>
public record DocumentResponse(Guid Id, string FileName, int PageCount, DateTimeOffset UploadedAt, Guid? CourseId);
