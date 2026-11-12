namespace Studeo.Api.Services;

public interface ISectioner
{
    /// <summary>
    /// Splits a document into sections: first by headings, otherwise into chunks of about
    /// 3,000 words. Sections always follow page boundaries (pages[0] = page 1).
    /// </summary>
    IReadOnlyList<SectionDraft> Split(IReadOnlyList<string> pages);
}

public record SectionDraft(string Title, int StartPage, int EndPage, int WordCount);
