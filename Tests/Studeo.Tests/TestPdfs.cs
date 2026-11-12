using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Studeo.Tests;

public static class TestPdfs
{
    /// <summary>The Greek sample from Studeo-server/samples (4 pages, 2 chapters).</summary>
    public static string GreekSamplePath => Path.Combine(AppContext.BaseDirectory, "samples", "biology-el.pdf");

    public static byte[] GreekSample() => File.ReadAllBytes(GreekSamplePath);

    /// <summary>Builds a PDF with one page per entry (Latin text only); an empty entry gives a blank page.</summary>
    public static byte[] Build(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(595, 842);
            if (text.Length > 0)
            {
                page.AddText(text, 12, new PdfPoint(60, 780), font);
            }
        }
        return builder.Build();
    }

    public static byte[] BlankPages(int count) => Build(Enumerable.Repeat(string.Empty, count).ToArray());

    public const string LongSentence = "Photosynthesis converts light energy into chemical energy in the chloroplasts.";
}
