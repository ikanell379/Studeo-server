using Studeo.Api.Contracts;
using Studeo.Api.Validators;

namespace Studeo.Tests.Unit;

public class CreateStudySetRequestValidatorTests
{
    private readonly CreateStudySetRequestValidator _validator = new();

    private bool IsValid(Guid sectionId, string difficulty, QuestionCounts counts) =>
        _validator.Validate(new CreateStudySetRequest(sectionId, difficulty, counts)).IsValid;

    [Theory]
    [InlineData("easy")]
    [InlineData("medium")]
    [InlineData("hard")]
    public void Valid_request_passes(string difficulty) =>
        Assert.True(IsValid(Guid.NewGuid(), difficulty, TestStudySets.SmallCounts));

    [Fact]
    public void Unknown_difficulty_fails() => Assert.False(IsValid(Guid.NewGuid(), "extreme", TestStudySets.SmallCounts));

    [Fact]
    public void Empty_section_fails() => Assert.False(IsValid(Guid.Empty, "easy", TestStudySets.SmallCounts));

    [Fact]
    public void All_zero_counts_fail() => Assert.False(IsValid(Guid.NewGuid(), "easy", new QuestionCounts(0, 0, 0, 0, 0, 0)));

    [Fact]
    public void Negative_or_too_large_count_fails()
    {
        Assert.False(IsValid(Guid.NewGuid(), "easy", new QuestionCounts(-1, 1, 1, 1, 1, 1)));
        Assert.False(IsValid(Guid.NewGuid(), "easy", new QuestionCounts(StudySetLimits.MaxPerType + 1, 1, 1, 1, 1, 1)));
    }

    [Fact]
    public void Missing_counts_fail() => Assert.False(IsValid(Guid.NewGuid(), "easy", null!));
}
