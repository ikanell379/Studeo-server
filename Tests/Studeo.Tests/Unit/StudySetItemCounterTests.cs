using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Tests.Unit;

public class StudySetItemCounterTests
{
    private static int CountIn(string json, int pieceSize)
    {
        var counter = new StudySetItemCounter();
        for (var i = 0; i < json.Length; i += pieceSize) counter.Append(json.Substring(i, Math.Min(pieceSize, json.Length - i)));
        return counter.Count;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(100_000)]
    public void Counts_every_item_whatever_the_piece_size(int pieceSize)
    {
        var json = TestStudySets.ModelJson(TestStudySets.Valid(TestStudySets.SmallCounts));

        Assert.Equal(StudySetService.TotalItems(TestStudySets.SmallCounts), CountIn(json, pieceSize));
    }

    [Fact]
    public void Ignores_page_inside_text_and_other_keys()
    {
        const string json = """
            {"theory":[{"concept":"\"page\": όχι κλειδί","explanation":"page:","pages":1,"page" : 4}],
             "x":"page","page2":3}
            """;

        Assert.Equal(1, CountIn(json, 2));
    }

    [Fact]
    public void Reports_changes_and_restarts_from_zero()
    {
        var counter = new StudySetItemCounter();
        var seen = new List<int>();
        counter.Changed += seen.Add;

        counter.Append("""[{"page":1},{"pa""");
        counter.Append("""ge":2}]""");
        counter.Restart();
        counter.Append("""[{"page":1}""");

        Assert.Equal([1, 2, 0, 1], seen);
        Assert.Equal(1, counter.Count);
    }

    [Theory]
    [InlineData(GenerationProgress.Reading, 0, 3)]
    [InlineData(GenerationProgress.Writing, 0, 5)]
    [InlineData(GenerationProgress.Writing, 5, 49)]
    [InlineData(GenerationProgress.Writing, 10, 93)]
    [InlineData(GenerationProgress.Writing, 99, 93)] // never past the total
    [InlineData(GenerationProgress.Saving, 10, 95)]
    public void Percent_follows_stage_and_finished_items(string stage, int done, int percent)
    {
        Assert.Equal(percent, GenerationProgress.For(stage, 1, done, 10).Percent);
    }
}
