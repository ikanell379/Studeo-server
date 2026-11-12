using Studeo.Api.Contracts;
using Studeo.Api.Services;
using Studeo.Api.Validators;

namespace Studeo.Tests.Unit;

public class FullCoverageTests
{
    [Fact]
    public void Short_section_gets_the_minimum_of_every_type()
    {
        Assert.Equal(new QuestionCounts(4, 2, 4, 1, 4, 3), FullCoverage.CountsFor(120));
        Assert.Equal(new QuestionCounts(4, 2, 4, 1, 4, 3), FullCoverage.CountsFor(0));
    }

    [Fact]
    public void Longer_section_gets_more_of_every_type_within_the_limits()
    {
        var medium = FullCoverage.CountsFor(1200);
        var longest = FullCoverage.CountsFor(3000);
        var huge = FullCoverage.CountsFor(50_000);

        Assert.Equal(new QuestionCounts(10, 3, 6, 1, 6, 4), medium);
        Assert.Equal(new QuestionCounts(15, 7, 15, 2, 15, 10), longest);
        Assert.Equal(new QuestionCounts(15, 10, 15, 3, 15, 10), huge);
        Assert.True(new[] { huge.Theory, huge.OpenQuestions, huge.TrueFalse, huge.Matching, huge.MultipleChoice, huge.FillBlanks }
            .All(n => n <= StudySetLimits.MaxPerType));
    }

    [Fact]
    public void Full_request_needs_no_counts_but_custom_does()
    {
        var validator = new CreateStudySetRequestValidator();

        Assert.True(validator.Validate(new CreateStudySetRequest(Guid.NewGuid(), "easy", null, StudySetModes.Full)).IsValid);
        Assert.False(validator.Validate(new CreateStudySetRequest(Guid.NewGuid(), "easy", null, StudySetModes.Custom)).IsValid);
        Assert.False(validator.Validate(new CreateStudySetRequest(Guid.NewGuid(), "easy", null)).IsValid);
        Assert.False(validator.Validate(new CreateStudySetRequest(Guid.NewGuid(), "easy", TestStudySets.SmallCounts, "everything")).IsValid);
        Assert.True(validator.Validate(new CreateStudySetRequest(Guid.NewGuid(), "easy", TestStudySets.SmallCounts)).IsValid);
    }
}
