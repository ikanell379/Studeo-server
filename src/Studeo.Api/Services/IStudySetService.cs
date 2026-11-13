using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

/// <summary>What is written alongside the questions, by the user's role.</summary>
public enum MaterialExtra
{
    None,

    /// <summary>Teachers: how to teach the section (<see cref="StudySet.TeachingGuide"/>).</summary>
    TeachingGuide,

    /// <summary>Students: a tutor's lesson with flashcards (<see cref="StudySet.Lesson"/>).</summary>
    Lesson,
}

public interface IStudySetService
{
    /// <summary>
    /// Generates study material for a section with the AI, validates it and stores it.
    /// <paramref name="progress"/> (optional) hears the real progress: stage and finished items. It is called
    /// synchronously, in order, from the generating code. The section must be in one of <paramref name="ownerId"/>'s
    /// documents (otherwise not_found). <paramref name="extra"/>: the teacher's guide or the student's lesson is
    /// written alongside; if only that fails, the material is saved without it.
    /// </summary>
    Task<StudySetResult> CreateAsync(
        CreateStudySetRequest request, string ownerId, MaterialExtra extra, IProgress<GenerationProgress>? progress,
        CancellationToken ct);

    /// <summary>Writes (or rewrites) the teaching guide of the owner's study set and saves it.</summary>
    Task<StudySetResult> AddTeachingGuideAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>Writes (or rewrites) the student's lesson of the owner's study set and saves it.</summary>
    Task<StudySetResult> AddLessonAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>A study set made from the owner's documents, or null.</summary>
    Task<StudySetResponse?> GetAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>Renames the owner's study set (name already validated); null if they have no such set.</summary>
    Task<StudySetResponse?> RenameAsync(Guid id, string ownerId, string name, CancellationToken ct);

    /// <summary>All study sets made from the owner's documents, newest first.</summary>
    Task<IReadOnlyList<StudySetSummaryResponse>> ListAsync(string ownerId, CancellationToken ct);
}

/// <summary>Either StudySet (success) or ErrorCode.</summary>
public record StudySetResult(StudySetResponse? StudySet, string? ErrorCode);
