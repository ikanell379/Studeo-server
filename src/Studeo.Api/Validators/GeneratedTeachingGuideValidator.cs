using FluentValidation;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

/// <summary>The model's teaching guide plus the section's pages, so key points can be checked against them.</summary>
public record GeneratedTeachingGuide(TeachingGuide Guide, int StartPage, int EndPage);

/// <summary>Validates the AI's teaching guide: every part present, sensible sizes, pages inside the section.</summary>
public class GeneratedTeachingGuideValidator : AbstractValidator<GeneratedTeachingGuide>
{
    public GeneratedTeachingGuideValidator()
    {
        RuleFor(g => g.Guide.Overview).NotEmpty().OverridePropertyName("overview");

        RuleFor(g => g.Guide.Objectives.Count)
            .InclusiveBetween(TeachingGuideLimits.MinObjectives, TeachingGuideLimits.MaxObjectives).OverridePropertyName("objectives count");
        RuleForEach(g => g.Guide.Objectives).NotEmpty().OverridePropertyName("objectives");

        RuleFor(g => g.Guide.Prerequisites.Count)
            .LessThanOrEqualTo(TeachingGuideLimits.MaxPrerequisites).OverridePropertyName("prerequisites count");
        RuleForEach(g => g.Guide.Prerequisites).NotEmpty().OverridePropertyName("prerequisites");

        RuleFor(g => g.Guide.LessonPlan.Count)
            .InclusiveBetween(TeachingGuideLimits.MinLessonSteps, TeachingGuideLimits.MaxLessonSteps).OverridePropertyName("lessonPlan count");
        RuleForEach(g => g.Guide.LessonPlan).ChildRules(s =>
        {
            s.RuleFor(x => x.Title).NotEmpty();
            s.RuleFor(x => x.Description).NotEmpty();
            s.RuleFor(x => x.Minutes).InclusiveBetween(1, TeachingGuideLimits.MaxStepMinutes);
        }).OverridePropertyName("lessonPlan");

        RuleFor(g => g.Guide.KeyPoints.Count)
            .InclusiveBetween(TeachingGuideLimits.MinKeyPoints, TeachingGuideLimits.MaxKeyPoints).OverridePropertyName("keyPoints count");
        RuleForEach(g => g.Guide.KeyPoints).ChildRules(p =>
        {
            p.RuleFor(x => x.Point).NotEmpty();
            p.RuleFor(x => x.HowToTeach).NotEmpty();
        }).OverridePropertyName("keyPoints");
        RuleFor(g => g)
            .Must(g => g.Guide.KeyPoints.All(p => p.Page >= g.StartPage && p.Page <= g.EndPage))
            .OverridePropertyName("keyPoints page")
            .WithMessage("Every key point's page must be inside the section's page range.");

        RuleFor(g => g.Guide.Misconceptions.Count)
            .LessThanOrEqualTo(TeachingGuideLimits.MaxMisconceptions).OverridePropertyName("misconceptions count");
        RuleForEach(g => g.Guide.Misconceptions).ChildRules(m =>
        {
            m.RuleFor(x => x.Mistake).NotEmpty();
            m.RuleFor(x => x.Correction).NotEmpty();
        }).OverridePropertyName("misconceptions");

        RuleFor(g => g.Guide.DiscussionQuestions.Count)
            .LessThanOrEqualTo(TeachingGuideLimits.MaxDiscussionQuestions).OverridePropertyName("discussionQuestions count");
        RuleForEach(g => g.Guide.DiscussionQuestions).NotEmpty().OverridePropertyName("discussionQuestions");
    }
}
