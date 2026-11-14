using Microsoft.Extensions.Logging.Abstractions;
using Studeo.Api.Contracts;
using Studeo.Api.Prompts;
using Studeo.Api.Services;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using static Studeo.Tests.TestOfficeFiles;
using M = DocumentFormat.OpenXml.Math;

namespace Studeo.Tests.Unit;

/// <summary>Formulas (Office Math → LaTeX), program code (kept with its indentation) and the kind of material.</summary>
public class FormulasAndCodeTests
{
    // ---- Office Math → LaTeX ----

    private static string Math(string inner) => $"<m:oMath xmlns:m=\"{MathNs}\">{inner}</m:oMath>";

    private static string R(string text) => $"<m:r><m:t>{text}</m:t></m:r>";

    private static string Latex(string inner) => OmmlLatex.Convert(new M.OfficeMath(Math(inner)));

    [Fact]
    public void Fractions_powers_and_greek_letters()
    {
        Assert.Equal(@"v=\frac{\Delta x}{\Delta t}", Latex(R("v=") + $"<m:f><m:num>{R("Δx")}</m:num><m:den>{R("Δt")}</m:den></m:f>"));
        Assert.Equal("E=m{c}^{2}", Latex(R("E=m") + $"<m:sSup><m:e>{R("c")}</m:e><m:sup>{R("2")}</m:sup></m:sSup>"));
        Assert.Equal("{x}_{1}", Latex($"<m:sSub><m:e>{R("x")}</m:e><m:sub>{R("1")}</m:sub></m:sSub>"));
    }

    [Fact]
    public void Roots_brackets_sums_and_functions()
    {
        Assert.Equal(@"\sqrt{x}", Latex($"<m:rad><m:radPr><m:degHide m:val=\"1\"/></m:radPr><m:deg/><m:e>{R("x")}</m:e></m:rad>"));
        Assert.Equal(@"\sqrt[3]{8}", Latex($"<m:rad><m:deg>{R("3")}</m:deg><m:e>{R("8")}</m:e></m:rad>"));
        Assert.Equal(@"\left( a+b \right)", Latex($"<m:d><m:e>{R("a+b")}</m:e></m:d>"));
        Assert.Equal(@"\left\{ x \right\}",
            Latex($"<m:d><m:dPr><m:begChr m:val=\"{{\"/><m:endChr m:val=\"}}\"/></m:dPr><m:e>{R("x")}</m:e></m:d>"));
        Assert.Equal(@"\sum_{i=1}^{n} {i}",
            Latex($"<m:nary><m:naryPr><m:chr m:val=\"∑\"/></m:naryPr><m:sub>{R("i=1")}</m:sub><m:sup>{R("n")}</m:sup><m:e>{R("i")}</m:e></m:nary>"));
        Assert.Equal(@"\sin {\theta }", Latex($"<m:func><m:fName>{R("sin")}</m:fName><m:e>{R("θ")}</m:e></m:func>"));
    }

    [Fact]
    public void Symbols_greek_function_names_and_prose()
    {
        Assert.Equal(@"F\cdot \Delta t\ge 0", Latex(R("F·Δt≥0")));
        Assert.Equal(@"\operatorname{ημ} x", Latex(R("ημx")));
        Assert.Equal(@"x>0\text{ όπου }", Latex(R("x>0") + "<m:r><m:t> όπου </m:t></m:r>"));
        Assert.Equal(@"\vec{F}", Latex($"<m:acc><m:accPr><m:chr m:val=\"⃗\"/></m:accPr><m:e>{R("F")}</m:e></m:acc>"));
        Assert.Equal(@"\begin{matrix}1 & 0 \\ 0 & 1\end{matrix}",
            Latex($"<m:m><m:mr><m:e>{R("1")}</m:e><m:e>{R("0")}</m:e></m:mr><m:mr><m:e>{R("0")}</m:e><m:e>{R("1")}</m:e></m:mr></m:m>"));
    }

    // ---- Word ----

    private readonly DocxTextExtractor _docx = new(NullLogger<DocxTextExtractor>.Instance);

    [Fact]
    public void Word_equations_become_latex_inline_and_on_their_own_line()
    {
        var sSup = $"<m:sSup><m:e>{R("c")}</m:e><m:sup>{R("2")}</m:sup></m:sSup>";
        var result = _docx.Extract(Docx(
            new Para("Η σχέση ", Math: Math(R("E=m") + sSup)),
            new Para("", Math: $"<m:oMathPara xmlns:m=\"{MathNs}\"><m:oMath>{R("F=ma")}</m:oMath></m:oMathPara>")), 50);

        Assert.Null(result.ErrorCode);
        Assert.Equal(["Η σχέση $E=m{c}^{2}$\n$$F=ma$$"], result.Pages);
    }

    [Fact]
    public void Word_code_keeps_its_indentation_in_a_fence()
    {
        var result = _docx.Extract(Docx(
            Text("Η συνάρτηση:"),
            new Para("def double(x):", Font: "Consolas"),
            new Para("    return x * 2", Font: "Consolas"),
            new Para("  y = 1", Style: "Code"),
            Text("Επιστρέφει το διπλάσιο.")), 50);

        Assert.Equal(["Η συνάρτηση:\n```\ndef double(x):\n    return x * 2\n  y = 1\n```\nΕπιστρέφει το διπλάσιο."], result.Pages);
    }

    // ---- PowerPoint ----

    private const string DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static string CodeLine(string text) =>
        $"<a:p xmlns:a=\"{DrawingNs}\"><a:r><a:rPr lang=\"en-US\"><a:latin typeface=\"Consolas\"/></a:rPr><a:t>{text}</a:t></a:r></a:p>";

    [Fact]
    public void PowerPoint_equations_and_code()
    {
        var equation =
            $"<a:p xmlns:a=\"{DrawingNs}\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" "
            + "xmlns:a14=\"http://schemas.microsoft.com/office/drawing/2010/main\">"
            + $"<mc:AlternateContent><mc:Choice Requires=\"a14\"><a14:m><m:oMathPara xmlns:m=\"{MathNs}\"><m:oMath>{R("F=ma")}</m:oMath></m:oMathPara></a14:m></mc:Choice>"
            + "<mc:Fallback><a:r><a:t>F=ma</a:t></a:r></mc:Fallback></mc:AlternateContent></a:p>";
        var pptx = new PptxTextExtractor(NullLogger<PptxTextExtractor>.Instance);

        var result = pptx.Extract(Pptx(new Slide("Νόμοι", [equation, CodeLine("for i in range(3):"), CodeLine("    print(i)")])), 50);

        Assert.Null(result.ErrorCode);
        Assert.Equal(["Νόμοι\n$F=ma$\n```\nfor i in range(3):\n    print(i)\n```"], result.Pages);
    }

    // ---- Text and PDF ----

    [Fact]
    public void Markdown_code_keeps_its_language_and_indentation()
    {
        var result = new TextFileExtractor().ExtractText("Παράδειγμα:\n```Python\ndef f():\n\n    return 1\n```\nΤέλος.", 50);

        Assert.Equal(["Παράδειγμα:\n```python\ndef f():\n\n    return 1\n```\nΤέλος."], result.Pages);
    }

    [Fact]
    public void Pdf_code_in_a_monospace_font_gets_its_indentation_back()
    {
        var builder = new PdfDocumentBuilder();
        var helvetica = builder.AddStandard14Font(Standard14Font.Helvetica);
        var courier = builder.AddStandard14Font(Standard14Font.Courier);
        var page = builder.AddPage(595, 842);
        page.AddText("The function doubles a number.", 12, new PdfPoint(60, 780), helvetica);
        page.AddText("def double(x):", 10, new PdfPoint(60, 750), courier);
        page.AddText("return x * 2", 10, new PdfPoint(60 + 4 * 6, 738), courier); // Courier 10pt: 6pt per letter
        page.AddText("print(double(3))", 10, new PdfPoint(60, 726), courier);

        var result = new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance).Extract(builder.Build(), 50);

        Assert.Contains("```\ndef double(x):\n    return x * 2\nprint(double(3))\n```", result.Pages[0]);
        Assert.StartsWith("The function doubles a number.", result.Pages[0]);
    }

    [Fact]
    public void A_page_of_code_is_not_taken_for_an_unreadable_scan()
    {
        var code = string.Join('\n', Enumerable.Repeat("x1 = f(a2, b3); y_2 = g(x1);", 10));

        Assert.True(PageQuality.IsUnreadable(code));
        Assert.False(PageQuality.IsUnreadable($"```c\n{code}\n```"));
    }

    // ---- Kind of material ----

    [Fact]
    public void Biology_with_a_chemical_equation_stays_general()
    {
        var pages = new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance).Extract(TestPdfs.GreekSample(), 50).Pages;

        Assert.Equal(MaterialSubject.General, SubjectDetector.Detect(pages));
    }

    [Fact]
    public void Physics_with_formulas_and_units_is_science()
    {
        var text = """
            Η μέση ταχύτητα ορίζεται ως $v = \frac{\Delta x}{\Delta t}$ και μετριέται σε m/s.
            Ένα αυτοκίνητο κινείται με v = 20 m/s για t = 5 s.
            Η απόσταση που διανύει είναι x = v · t = 100 m.
            Αν η μάζα του είναι 1200 kg και η επιτάχυνση 2 m/s², η δύναμη είναι F = m · a = 2400 N.
            Η κινητική ενέργεια δίνεται από τη σχέση $K = \frac{1}{2} m v^2$.
            """;

        Assert.Equal(Subjects.Science, SubjectDetector.Detect([text]).Subject);
    }

    [Theory]
    [InlineData("Η συνάρτηση:\n```python\ndef f(x):\n    if x > 0:\n        return x\n    return -x\nprint(f(-3))\nprint(f(2))\n```", "python")]
    [InlineData("Το πρόγραμμα:\n#include <stdio.h>\nint main() {\n    int x = 5;\n    printf(\"%d\", x);\n    return 0;\n}\nΤυπώνει 5.", "c")]
    [InlineData("ΠΡΟΓΡΑΜΜΑ Άθροισμα\nΜΕΤΑΒΛΗΤΕΣ\nΑΚΕΡΑΙΕΣ: i, s\nΑΡΧΗ\ns <- 0\nΓΙΑ i ΑΠΟ 1 ΜΕΧΡΙ 10\ns <- s + i\nΤΕΛΟΣ_ΕΠΑΝΑΛΗΨΗΣ\nΓΡΑΨΕ s\nΤΕΛΟΣ_ΠΡΟΓΡΑΜΜΑΤΟΣ", ProgrammingLanguages.Glossa)]
    public void Code_is_programming_with_its_language(string text, string language)
    {
        Assert.Equal(new MaterialSubject(Subjects.Programming, language), SubjectDetector.Detect([text]));
    }

    [Fact]
    public void Greek_questions_ending_in_a_question_mark_are_not_code()
    {
        var text = string.Join('\n', Enumerable.Range(1, 12).Select(i => $"Τι είναι η φωτοσύνθεση ({i});"));

        Assert.Equal(MaterialSubject.General, SubjectDetector.Detect([text]));
    }

    [Fact]
    public void The_users_choice_wins_and_missing_parts_come_from_the_text()
    {
        string[] code = ["```java\npublic class A {\n  public static void main(String[] a) {\n    int x = 1;\n    System.out.println(x);\n  }\n}\n```"];
        string[] littleCode = ["Η εντολή:\n```java\nSystem.out.println(1);\n```\nτυπώνει 1."];
        var id = Guid.NewGuid();

        Assert.Equal(new MaterialSubject(Subjects.Science), StudySetService.SubjectFor(new(id, "easy", null, Subject: Subjects.Science), code));
        Assert.Equal(new MaterialSubject(Subjects.Programming, "java"), StudySetService.SubjectFor(new(id, "easy", null, Subject: Subjects.Programming), littleCode));
        Assert.Equal(MaterialSubject.General, SubjectDetector.Detect(littleCode));
        Assert.Equal(new MaterialSubject(Subjects.Programming, "java"), StudySetService.SubjectFor(new(id, "easy", null), code));
        Assert.Equal(new MaterialSubject(Subjects.Programming, "python"),
            StudySetService.SubjectFor(new(id, "easy", null, Subject: Subjects.Programming, Language: "python"), code));
        Assert.Equal(new MaterialSubject(Subjects.General), StudySetService.SubjectFor(new(id, "easy", null, Subject: Subjects.General, Language: "java"), code));
    }

    // ---- Prompts ----

    [Fact]
    public void Prompts_ask_for_latex_and_code_fences_and_name_the_kind_of_material()
    {
        Assert.Contains(@"\ce{", GenerateStudySetPrompt.System);
        Assert.Contains("```python", StudentLessonPrompt.System);
        Assert.Contains("$…$", TeachingGuidePrompt.System);

        var counts = new QuestionCounts(1, 0, 0, 0, 0, 0);
        Assert.DoesNotContain("Είδος υλικού", GenerateStudySetPrompt.User("Τ", "κείμενο", "easy", counts));
        Assert.Contains("θετικές επιστήμες", GenerateStudySetPrompt.User("Τ", "κείμενο", "easy", counts, subject: new MaterialSubject(Subjects.Science)));
        var glossa = StudentLessonPrompt.User("Τ", "κείμενο", "easy", 3, 6, new MaterialSubject(Subjects.Programming, ProgrammingLanguages.Glossa));
        Assert.Contains("προγραμματισμός (γλώσσα: ΓΛΩΣΣΑ (ΑΕΠΠ))", glossa);
        Assert.Contains("```glossa", glossa);
    }
}
