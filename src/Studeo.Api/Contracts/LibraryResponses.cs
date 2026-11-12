namespace Studeo.Api.Contracts;

/// <summary>A PDF the user uploaded, for "Τα αρχεία μου" (GET /api/documents), newest first.</summary>
public record DocumentSummaryResponse(
    Guid Id, string FileName, int PageCount, DateTimeOffset UploadedAt, int StudySetCount, Guid? CourseId);

/// <summary>Study material the user made, for "Τα κουίζ μου" (GET /api/study-sets), newest first.</summary>
public record StudySetSummaryResponse(
    Guid Id,
    string Name,
    Guid DocumentId,
    string FileName,
    Guid SectionId,
    string SectionTitle,
    string Difficulty,
    DateTimeOffset CreatedAt);
