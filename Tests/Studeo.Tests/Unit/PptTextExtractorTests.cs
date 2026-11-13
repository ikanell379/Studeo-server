using Microsoft.Extensions.Logging.Abstractions;
using OpenMcdf;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Tests.Unit;

public class PptTextExtractorTests
{
    private readonly PptTextExtractor _extractor = new(NullLogger<PptTextExtractor>.Instance);

    /// <summary>Saved by PowerPoint as 97–2003: 4 slides, Greek titles and bullets, a text box on slide 3, notes on 1 and 3.</summary>
    private static byte[] Sample() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "samples", "slides-el.ppt"));

    [Fact]
    public void Reads_each_slide_with_its_title_first_and_its_notes_last()
    {
        var result = _extractor.Extract(Sample(), DocumentLimits.MaxPages);

        Assert.Null(result.ErrorCode);
        Assert.Equal(4, result.Pages.Count);
        Assert.Equal(
            "Το κύτταρο\nΗ βασική μονάδα της ζωής.\nΌλοι οι οργανισμοί αποτελούνται από κύτταρα.\nΘυμίστε τον ορισμό του κυττάρου.",
            result.Pages[0]);
        Assert.Equal("Το κύτταρο\nΠροκαρυωτικά κύτταρα: χωρίς πυρήνα.\nΕυκαρυωτικά κύτταρα: με πυρήνα και οργανίδια.", result.Pages[1]);
        Assert.Equal(
            "Η φωτοσύνθεση\nΓίνεται στους χλωροπλάστες.\nΜετατρέπει τη φωτεινή ενέργεια σε χημική.\nΠλαίσιο: η εξίσωση της φωτοσύνθεσης.\nΔείξτε το σχήμα του χλωροπλάστη.",
            result.Pages[2]);
        Assert.Equal(
            [(1, "Το κύτταρο"), (2, "Το κύτταρο"), (3, "Η φωτοσύνθεση"), (4, "Η φωτοσύνθεση")],
            result.Headings.Select(h => (h.Page, h.Title)));
    }

    [Fact]
    public void Too_many_slides_fails()
    {
        Assert.Equal(ErrorCodes.TooManyPages, _extractor.Extract(Sample(), maxPages: 3).ErrorCode);
    }

    [Fact]
    public void Other_files_are_invalid()
    {
        Assert.Equal(ErrorCodes.InvalidFile, _extractor.Extract("not a ppt"u8.ToArray(), 50).ErrorCode);
        // A .pptx renamed to .ppt.
        var pptx = TestOfficeFiles.Pptx(new TestOfficeFiles.Slide("Τίτλος", ["κείμενο"]));
        Assert.Equal(ErrorCodes.InvalidFile, _extractor.Extract(pptx, 50).ErrorCode);
        // A compound file that isn't a presentation (like a Word .doc renamed to .ppt).
        Assert.Equal(ErrorCodes.InvalidFile, _extractor.Extract(CompoundFile("WordDocument"), 50).ErrorCode);
        // A presentation whose records are cut short.
        var damaged = CompoundFile("PowerPoint Document", "Current User");
        Assert.Equal(ErrorCodes.InvalidFile, _extractor.Extract(damaged, 50).ErrorCode);
    }

    [Fact]
    public void Cut_short_sample_is_invalid_not_a_crash()
    {
        var sample = Sample();
        Assert.Equal(ErrorCodes.InvalidFile, _extractor.Extract(sample[..(sample.Length / 2)], 50).ErrorCode);
    }

    private static byte[] CompoundFile(params string[] streams)
    {
        using var buffer = new MemoryStream();
        using (var root = RootStorage.Create(buffer, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            foreach (var name in streams)
            {
                using var stream = root.CreateStream(name);
                stream.Write(new byte[64]);
            }
        }
        return buffer.ToArray();
    }
}
