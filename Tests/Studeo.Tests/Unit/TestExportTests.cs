using DocumentFormat.OpenXml.Packaging;
using Studeo.Api.Contracts;
using Studeo.Api.Services.Export;
using UglyToad.PdfPig;

namespace Studeo.Tests.Unit;

public class TestExportTests
{
    private static readonly StudySet Set = WithIds(TestStudySets.Valid(new QuestionCounts(1, 2, 2, 1, 2, 1), page: 3) with
    {
        SectionTitle = "Κεφάλαιο 2: Η φωτοσύνθεση",
    });

    private static StudySet WithIds(StudySet s) => Api.Services.StudySetService.WithIds(s);

    private static IEnumerable<string> AllText(TestSheet sheet) =>
        sheet.Parts.SelectMany(p => p.Questions)
            .SelectMany(q => new[] { q.Text, q.Answer ?? "" }.Concat(q.Options).Concat(q.Matching?.Left ?? []).Concat(q.Matching?.Right ?? []));

    [Fact]
    public void Worksheet_has_every_type_in_order_and_no_answers()
    {
        var sheet = TestSheetBuilder.Build(Set, "Διαγώνισμα", answerKey: false, showPages: false, types: null);

        Assert.Equal(
            ["Α. Σωστό ή Λάθος", "Β. Ερωτήσεις πολλαπλής επιλογής", "Γ. Συμπλήρωση κενών", "Δ. Αντιστοίχιση", "Ε. Ερωτήσεις ανάπτυξης"],
            sheet.Parts.Select(p => p.Heading));
        Assert.Equal("Κεφάλαιο 2: Η φωτοσύνθεση", sheet.Subtitle);
        var questions = sheet.Parts.SelectMany(p => p.Questions).ToList();
        Assert.All(questions, q => Assert.Null(q.Answer));
        Assert.All(questions, q => Assert.Null(q.Page));
        Assert.DoesNotContain(AllText(sheet), t => t.Contains("Γιατί") || t.Contains("Επειδή") || t.Contains("μιτοχόνδριο"));
        Assert.Contains(TestSheetBuilder.WriteBlank, sheet.Parts[2].Questions[0].Text);
        Assert.Equal(["α) α", "β) β", "γ) γ", "δ) δ"], sheet.Parts[1].Questions[0].Options);
        Assert.Equal(6, sheet.Parts[4].Questions[0].AnswerLines);
        Assert.Equal("Σ  /  Λ", sheet.Parts[0].Questions[0].Mark);
    }

    [Fact]
    public void Answer_key_has_the_answers_and_chosen_types_with_pages()
    {
        var sheet = TestSheetBuilder.Build(Set, "Διαγώνισμα", answerKey: true, showPages: true,
            types: [TestQuestionTypes.MultipleChoice, TestQuestionTypes.FillBlanks]);

        Assert.Equal(["Α. Ερωτήσεις πολλαπλής επιλογής", "Β. Συμπλήρωση κενών"], sheet.Parts.Select(p => p.Heading));
        Assert.Equal("Απαντήσεις", sheet.Subtitle);
        Assert.Equal("γ) γ. Επειδή.", sheet.Parts[0].Questions[0].Answer);
        Assert.Equal("μιτοχόνδριο", sheet.Parts[1].Questions[0].Answer);
        Assert.All(sheet.Parts.SelectMany(p => p.Questions), q => Assert.Equal(3, q.Page));
        Assert.All(sheet.Parts.SelectMany(p => p.Questions), q => Assert.Equal(0, q.AnswerLines));
    }

    [Fact]
    public void Matching_is_shuffled_the_same_way_in_test_and_answers_and_the_key_is_right()
    {
        var test = TestSheetBuilder.Build(Set, "Τ", false, false, [TestQuestionTypes.Matching]).Parts[0].Questions[0];
        var key = TestSheetBuilder.Build(Set, "Τ", true, false, [TestQuestionTypes.Matching]).Parts[0].Questions[0];

        Assert.Equal(test.Matching!.Right, key.Matching!.Right);
        Assert.NotEqual(["α. Β1", "β. Β2", "γ. Β3", "δ. Β4"], test.Matching.Right);
        // Every "n → letter" points at Bn (the pairs are Αn ↔ Βn).
        foreach (var link in key.Answer!.Split(",  "))
        {
            var (number, letter) = (link.Split(" → ")[0], link.Split(" → ")[1]);
            Assert.Contains($"{letter}. Β{number}", key.Matching.Right);
        }
    }

    [Fact]
    public void Shuffle_never_keeps_the_original_order()
    {
        foreach (var seed in Enumerable.Range(0, 200).Select(i => $"seed{i}"))
        {
            Assert.NotEqual([0, 1, 2, 3], TestSheetBuilder.ShuffledOrder(4, seed));
        }
    }

    [Theory]
    [InlineData("Τεστ: Κεφ. 2/3?", false, "pdf", "Τεστ Κεφ. 23.pdf")]
    [InlineData("Φωτοσύνθεση", true, "docx", "Φωτοσύνθεση - Απαντήσεις.docx")]
    [InlineData("  ", false, "pdf", "Τεστ.pdf")]
    public void File_names_are_safe(string title, bool answers, string ext, string expected)
    {
        Assert.Equal(expected, TestSheetBuilder.FileName(title, answers, ext));
    }

    [Fact]
    public void Pdf_has_the_greek_text()
    {
        var bytes = PdfTestRenderer.Render(TestSheetBuilder.Build(Set, "Διαγώνισμα φυσικής", false, true, null));

        using var pdf = PdfDocument.Open(bytes);
        var text = string.Join(" ", pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Διαγώνισμα", text);
        Assert.Contains("Όνομα", text);
        Assert.Contains("Αντιστοίχιση", text);
        Assert.DoesNotContain("Απάντηση:", text);
    }

    [Fact]
    public void Answer_key_pdf_has_the_answers()
    {
        var bytes = PdfTestRenderer.Render(TestSheetBuilder.Build(Set, "Διαγώνισμα", true, false, null));

        using var pdf = PdfDocument.Open(bytes);
        var text = string.Join(" ", pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Απάντηση:", text);
        Assert.Contains("μιτοχόνδριο", text);
        Assert.DoesNotContain("Τμήμα", text);
    }

    [Fact]
    public void Docx_opens_in_word_format_with_the_questions()
    {
        var bytes = DocxTestRenderer.Render(TestSheetBuilder.Build(Set, "Διαγώνισμα", false, false, null));

        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("Διαγώνισμα", text);
        Assert.Contains("Ημερομηνία", text);
        Assert.Contains("Ερώτηση 1;", text);
        Assert.Contains("Στήλη Β", text);
        Assert.DoesNotContain("Απάντηση:", text);
    }
}
