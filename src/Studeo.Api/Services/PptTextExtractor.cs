using System.Buffers.Binary;
using System.Text;
using OpenMcdf;
using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

/// <summary>
/// The text of an old PowerPoint 97–2003 presentation (.ppt), one page per slide, like <see cref="PptxTextExtractor"/>:
/// the title first, then the slide's other text, then the speaker notes.
/// A .ppt is an OLE compound file (read with OpenMcdf) whose «PowerPoint Document» stream is a tree of binary records
/// ([MS-PPT]). The «Current User» stream points to the latest edit, whose persist directory maps object ids to
/// offsets: the document, and from its slide list each slide and its notes. Placeholder text (title, body) is kept in
/// the document's slide list; text of other shapes is inside the slide's own record.
/// </summary>
public class PptTextExtractor(ILogger<PptTextExtractor> logger)
{
    public TextExtractionResult Extract(byte[] content, int maxPages)
    {
        if (!IsCompoundFile(content))
        {
            return TextExtractionResult.Fail(ErrorCodes.InvalidFile);
        }

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var root = RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
            if (!root.TryOpenStream("PowerPoint Document", out var documentStream)
                || !root.TryOpenStream("Current User", out var userStream))
            {
                return TextExtractionResult.Fail(ErrorCodes.InvalidFile); // e.g. a Word .doc renamed to .ppt
            }

            var slides = PptRecords.Slides(ReadAll(documentStream), ReadAll(userStream));
            if (slides.Count > maxPages)
            {
                return TextExtractionResult.Fail(ErrorCodes.TooManyPages);
            }

            var pages = new List<string>(slides.Count);
            var headings = new List<StyledHeading>();
            foreach (var slide in slides)
            {
                pages.Add(OfficeFiles.Clean([slide.Title ?? "", .. slide.Body, .. slide.Notes]));
                if (slide.Title is not null)
                {
                    headings.Add(new StyledHeading(pages.Count, PptxTextExtractor.Truncate(slide.Title), AtTop: true));
                }
            }
            return new TextExtractionResult(pages, null) { Headings = headings };
        }
        catch (Exception ex)
        {
            // Damaged, password-protected (encrypted) or not a presentation at all.
            logger.LogWarning(ex, "Could not read PPT");
            return TextExtractionResult.Fail(ErrorCodes.InvalidFile);
        }
    }

    /// <summary>The OLE compound file signature (D0 CF 11 E0 A1 B1 1A E1).</summary>
    internal static bool IsCompoundFile(byte[] content) =>
        content.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);

    private static byte[] ReadAll(CfbStream stream)
    {
        using (stream)
        {
            if (stream.Length > OfficeFiles.MaxCharactersInPart)
            {
                throw new InvalidDataException("Stream too large.");
            }
            var buffer = new MemoryStream((int)stream.Length);
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}

/// <summary>A slide's title (null if it has none), its other lines and its speaker notes.</summary>
internal record PptSlide(string? Title, List<string> Body, List<string> Notes);

/// <summary>Reads slides out of the binary records of a «PowerPoint Document» stream ([MS-PPT]).</summary>
internal static class PptRecords
{
    public const int DocumentContainer = 0x03E8;
    public const int SlideContainer = 0x03EE;
    public const int SlideAtom = 0x03EF;
    public const int NotesContainer = 0x03F0;
    public const int SlidePersistAtom = 0x03F3;
    public const int TextHeaderAtom = 0x0F9F;
    public const int TextCharsAtom = 0x0FA0;
    public const int TextBytesAtom = 0x0FA8;
    public const int SlideListWithText = 0x0FF0;
    public const int UserEditAtom = 0x0FF5;
    public const int PersistDirectoryAtom = 0x1772;

    /// <summary>TextHeaderAtom text types: title, centred title (title slide), notes.</summary>
    public const uint TitleText = 0, CenterTitleText = 6, NotesText = 2;

    /// <summary>SlideListWithText instance: 0 = the slides, 2 = the notes pages.</summary>
    private const int SlidesList = 0, NotesList = 2;

    private const int MaxDepth = 32;

    /// <summary>A record header: version (0xF = container), instance, type and length of the data after it.</summary>
    internal readonly record struct Record(int Offset, int Version, int Instance, int Type, int Length)
    {
        public int Data => Offset + 8;
        public int End => Data + Length;
        public bool IsContainer => Version == 0xF;
    }

    public static List<PptSlide> Slides(byte[] document, byte[] currentUser)
    {
        var persist = PersistDirectory(document, currentUser, out var documentId);
        var root = At(document, persist, documentId, DocumentContainer);
        var lists = Children(document, root).Where(r => r.Type == SlideListWithText).ToList();
        var notesBySlideId = SlideList(document, lists.Where(r => r.Instance == NotesList).Cast<Record?>().FirstOrDefault())
            .GroupBy(n => n.SlideId)
            .ToDictionary(g => g.Key, g => g.First());

        var slides = new List<PptSlide>();
        foreach (var entry in SlideList(document, lists.Where(r => r.Instance == SlidesList).Cast<Record?>().FirstOrDefault()))
        {
            var texts = new List<(uint Type, string Text)>(entry.Texts);
            uint? notesId = null;
            if (persist.TryGetValue(entry.PersistId, out var offset) && Read(document, offset) is { Type: SlideContainer } container)
            {
                Collect(document, container, texts, 0);
                notesId = Children(document, container).Where(r => r.Type == SlideAtom && r.Length >= 20)
                    .Select(r => (uint?)U32(document, r.Data + 16)).FirstOrDefault();
            }

            var lines = Distinct(texts);
            var title = lines.FirstOrDefault(t => t.Type is TitleText or CenterTitleText);
            var notes = new List<string>();
            if (notesId is { } id and not 0 && notesBySlideId.TryGetValue(id, out var notesEntry))
            {
                var noteTexts = new List<(uint Type, string Text)>(notesEntry.Texts);
                if (persist.TryGetValue(notesEntry.PersistId, out var notesOffset)
                    && Read(document, notesOffset) is { Type: NotesContainer } notesContainer)
                {
                    Collect(document, notesContainer, noteTexts, 0);
                }
                notes = Distinct(noteTexts).Where(t => t.Type == NotesText).SelectMany(t => Lines(t.Text)).ToList();
            }

            slides.Add(new PptSlide(
                title.Text is null ? null : string.Join(' ', Lines(title.Text)),
                lines.Where(t => !ReferenceEquals(t.Text, title.Text)).SelectMany(t => Lines(t.Text)).ToList(),
                notes));
        }
        return slides;
    }

    /// <summary>
    /// Object id → offset in the document stream, newest edit first (an id saved again later wins).
    /// Throws for an encrypted presentation: its records can't be read.
    /// </summary>
    internal static Dictionary<uint, int> PersistDirectory(byte[] document, byte[] currentUser, out uint documentId)
    {
        // CurrentUserAtom: header (8), size (4), headerToken (4), offsetToCurrentEdit (4).
        var offset = (int)U32(currentUser, 16);
        var map = new Dictionary<uint, int>();
        var visited = new HashSet<int>();
        documentId = 0;
        var newest = true;
        while (visited.Add(offset))
        {
            var edit = Read(document, offset);
            if (edit.Type != UserEditAtom || edit.Length < 0x1C)
            {
                throw new InvalidDataException("No UserEditAtom.");
            }
            if (newest)
            {
                documentId = U32(document, edit.Data + 16);
                if (edit.Length >= 0x20 && U32(document, edit.Data + 28) != 0)
                {
                    throw new InvalidDataException("Encrypted presentation.");
                }
                newest = false;
            }

            var directory = Read(document, (int)U32(document, edit.Data + 12));
            if (directory.Type != PersistDirectoryAtom)
            {
                throw new InvalidDataException("No PersistDirectoryAtom.");
            }
            for (var p = directory.Data; p + 4 <= directory.End;)
            {
                var head = U32(document, p);
                p += 4;
                var first = head & 0xFFFFF;
                for (var i = 0u; i < head >> 20; i++, p += 4)
                {
                    if (p + 4 > directory.End)
                    {
                        throw new InvalidDataException("Persist directory cut short.");
                    }
                    map.TryAdd(first + i, (int)U32(document, p));
                }
            }

            var previous = (int)U32(document, edit.Data + 8);
            if (previous == 0)
            {
                break;
            }
            offset = previous;
        }
        return map;
    }

    private record ListEntry(uint PersistId, uint SlideId, List<(uint Type, string Text)> Texts);

    /// <summary>A SlideListWithText: each SlidePersistAtom, followed by the text of that slide's placeholders.</summary>
    private static List<ListEntry> SlideList(byte[] document, Record? list)
    {
        var entries = new List<ListEntry>();
        if (list is not { } container)
        {
            return entries;
        }
        uint type = uint.MaxValue;
        foreach (var record in Children(document, container))
        {
            switch (record.Type)
            {
                case SlidePersistAtom when record.Length >= 16:
                    entries.Add(new ListEntry(U32(document, record.Data), U32(document, record.Data + 12), []));
                    type = uint.MaxValue;
                    break;
                case TextHeaderAtom when record.Length >= 4:
                    type = U32(document, record.Data);
                    break;
                case TextCharsAtom or TextBytesAtom when entries.Count > 0:
                    entries[^1].Texts.Add((type, Decode(document, record)));
                    break;
            }
        }
        return entries;
    }

    /// <summary>All text atoms inside a slide or notes container (its shapes' text boxes), in order.</summary>
    private static void Collect(byte[] document, Record container, List<(uint Type, string Text)> texts, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidDataException("Records nested too deep.");
        }
        uint type = uint.MaxValue;
        foreach (var record in Children(document, container))
        {
            if (record.IsContainer)
            {
                Collect(document, record, texts, depth + 1);
            }
            else if (record.Type == TextHeaderAtom && record.Length >= 4)
            {
                type = U32(document, record.Data);
            }
            else if (record.Type is TextCharsAtom or TextBytesAtom)
            {
                texts.Add((type, Decode(document, record)));
            }
        }
    }

    /// <summary>
    /// Leaves out empty texts and texts already seen on the slide (a placeholder's text can be stored both in the
    /// slide list and in the slide itself).
    /// </summary>
    private static List<(uint Type, string Text)> Distinct(List<(uint Type, string Text)> texts)
    {
        var seen = new HashSet<string>();
        return texts.Where(t => t.Text.Trim().Length > 0 && seen.Add(t.Text.Trim())).ToList();
    }

    /// <summary>Paragraphs end with \r and line breaks are \v.</summary>
    private static IEnumerable<string> Lines(string text) =>
        text.Split(['\r', '\n', '\v'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>TextCharsAtom: UTF-16; TextBytesAtom: the low bytes of UTF-16 characters (Latin-1).</summary>
    private static string Decode(byte[] document, Record record) =>
        record.Type == TextCharsAtom
            ? Encoding.Unicode.GetString(document, record.Data, record.Length & ~1)
            : Encoding.Latin1.GetString(document, record.Data, record.Length);

    private static Record At(byte[] document, Dictionary<uint, int> persist, uint id, int type)
    {
        var record = persist.TryGetValue(id, out var offset)
            ? Read(document, offset)
            : throw new InvalidDataException($"Object {id} not in the persist directory.");
        return record.Type == type ? record : throw new InvalidDataException($"Record 0x{type:X4} not found.");
    }

    internal static Record Read(byte[] data, int offset)
    {
        if (offset < 0 || offset > data.Length - 8)
        {
            throw new InvalidDataException("Record outside the stream.");
        }
        var versionAndInstance = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        var type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2));
        var length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
        if (length > (uint)(data.Length - offset - 8))
        {
            throw new InvalidDataException("Record longer than the stream.");
        }
        return new Record(offset, versionAndInstance & 0xF, versionAndInstance >> 4, type, (int)length);
    }

    private static IEnumerable<Record> Children(byte[] data, Record parent)
    {
        for (var position = parent.Data; position + 8 <= parent.End;)
        {
            var child = Read(data, position);
            if (child.End > parent.End)
            {
                throw new InvalidDataException("Record longer than its container.");
            }
            yield return child;
            position = child.End;
        }
    }

    private static uint U32(byte[] data, int offset)
    {
        if (offset < 0 || offset > data.Length - 4)
        {
            throw new InvalidDataException("Read outside the stream.");
        }
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    }
}
