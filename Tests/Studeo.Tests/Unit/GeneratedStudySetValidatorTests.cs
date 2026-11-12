using Studeo.Api.Contracts;
using Studeo.Api.Validators;

namespace Studeo.Tests.Unit;

public class GeneratedStudySetValidatorTests
{
    private static readonly QuestionCounts Counts = TestStudySets.SmallCounts;
    private readonly GeneratedStudySetValidator _validator = new();

    private bool IsValid(StudySet s, int start = 1, int end = 3) =>
        _validator.Validate(new GeneratedStudySet(s, Counts, start, end)).IsValid;

    [Fact]
    public void Valid_set_passes() => Assert.True(IsValid(TestStudySets.Valid(Counts, page: 2)));

    [Fact]
    public void Wrong_count_fails()
    {
        var s = TestStudySets.Valid(Counts);
        Assert.False(IsValid(s with { TrueFalse = s.TrueFalse.Take(1).ToList() }));
    }

    [Fact]
    public void Multiple_choice_needs_four_unique_options_and_index_in_range()
    {
        var s = TestStudySets.Valid(Counts);
        var mc = s.MultipleChoice[0];

        Assert.False(IsValid(s with { MultipleChoice = [mc with { Options = ["α", "β", "γ"] }, s.MultipleChoice[1]] }));
        Assert.False(IsValid(s with { MultipleChoice = [mc with { Options = ["α", "α", "γ", "δ"] }, s.MultipleChoice[1]] }));
        Assert.False(IsValid(s with { MultipleChoice = [mc with { CorrectIndex = 4 }, s.MultipleChoice[1]] }));
    }

    [Theory]
    [InlineData("Χωρίς κενό.")]
    [InlineData("Δύο ____ και ____.")]
    public void Fill_blank_needs_exactly_one_blank(string sentence)
    {
        var s = TestStudySets.Valid(Counts);
        Assert.False(IsValid(s with { FillBlanks = [s.FillBlanks[0] with { Sentence = sentence }] }));
    }

    [Fact]
    public void Matching_needs_four_to_six_unique_pairs()
    {
        var s = TestStudySets.Valid(Counts);
        var m = s.Matching[0];

        Assert.False(IsValid(s with { Matching = [m with { Pairs = m.Pairs.Take(3).ToList() }] }));
        Assert.False(IsValid(s with
        {
            Matching = [m with { Pairs = [.. m.Pairs.Take(3), new MatchingPair { Left = "Α1", Right = "Β9" }] }],
        }));
    }

    [Fact]
    public void Page_outside_section_fails()
    {
        Assert.False(IsValid(TestStudySets.Valid(Counts, page: 4), start: 1, end: 3));
        Assert.False(IsValid(TestStudySets.Valid(Counts, page: 0), start: 1, end: 3));
    }

    [Fact]
    public void Empty_text_fails()
    {
        var s = TestStudySets.Valid(Counts);
        Assert.False(IsValid(s with { OpenQuestions = [s.OpenQuestions[0] with { Answer = "" }] }));
    }
}
