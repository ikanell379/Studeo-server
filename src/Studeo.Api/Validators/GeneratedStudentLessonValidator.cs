using FluentValidation;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

/// <summary>The model's lesson plus the section's pages, so steps and cards can be checked against them.</summary>
public record GeneratedStudentLesson(StudentLesson Lesson, int StartPage, int EndPage);

/// <summary>Validates the AI's lesson: every part present, sensible sizes, pages inside the section.</summary>
public class GeneratedStudentLessonValidator : AbstractValidator<GeneratedStudentLesson>
{
    public GeneratedStudentLessonValidator()
    {
        RuleFor(g => g.Lesson.Intro).NotEmpty().OverridePropertyName("intro");

        RuleFor(g => g.Lesson.Goals.Count)
            .InclusiveBetween(StudentLessonLimits.MinGoals, StudentLessonLimits.MaxGoals).OverridePropertyName("goals count");
        RuleForEach(g => g.Lesson.Goals).NotEmpty().OverridePropertyName("goals");

        RuleFor(g => g.Lesson.Steps.Count)
            .InclusiveBetween(StudentLessonLimits.MinSteps, StudentLessonLimits.MaxSteps).OverridePropertyName("steps count");
        RuleForEach(g => g.Lesson.Steps).ChildRules(s =>
        {
            s.RuleFor(x => x.Title).NotEmpty();
            s.RuleFor(x => x.Explanation).NotEmpty();
            s.RuleFor(x => x.Check.Question).NotEmpty();
            s.RuleFor(x => x.Check.Answer).NotEmpty();
        }).OverridePropertyName("steps");

        RuleFor(g => g.Lesson.Flashcards.Count)
            .InclusiveBetween(StudentLessonLimits.MinFlashcards, StudentLessonLimits.MaxFlashcards).OverridePropertyName("flashcards count");
        RuleForEach(g => g.Lesson.Flashcards).ChildRules(c =>
        {
            c.RuleFor(x => x.Front).NotEmpty();
            c.RuleFor(x => x.Back).NotEmpty();
        }).OverridePropertyName("flashcards");

        RuleFor(g => g)
            .Must(g => g.Lesson.Steps.All(s => s.Page >= g.StartPage && s.Page <= g.EndPage)
                && g.Lesson.Flashcards.All(c => c.Page >= g.StartPage && c.Page <= g.EndPage))
            .OverridePropertyName("page")
            .WithMessage("Every step's and card's page must be inside the section's page range.");

        RuleFor(g => g.Lesson.MemoryTips.Count)
            .LessThanOrEqualTo(StudentLessonLimits.MaxMemoryTips).OverridePropertyName("memoryTips count");
        RuleForEach(g => g.Lesson.MemoryTips).NotEmpty().OverridePropertyName("memoryTips");

        RuleFor(g => g.Lesson.Mistakes.Count)
            .LessThanOrEqualTo(StudentLessonLimits.MaxMistakes).OverridePropertyName("mistakes count");
        RuleForEach(g => g.Lesson.Mistakes).ChildRules(m =>
        {
            m.RuleFor(x => x.Mistake).NotEmpty();
            m.RuleFor(x => x.Correction).NotEmpty();
        }).OverridePropertyName("mistakes");

        RuleFor(g => g.Lesson.Summary.Count)
            .InclusiveBetween(StudentLessonLimits.MinSummary, StudentLessonLimits.MaxSummary).OverridePropertyName("summary count");
        RuleForEach(g => g.Lesson.Summary).NotEmpty().OverridePropertyName("summary");
    }
}
