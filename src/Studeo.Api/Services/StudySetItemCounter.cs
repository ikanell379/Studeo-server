namespace Studeo.Api.Services;

/// <summary>
/// Counts finished items while the study-set JSON streams in. Every item (theory, question, matching…)
/// ends with its "page" field (see GenerateStudySetPrompt.OutputSchema), so each "page" key = one item done.
/// Works on pieces of any size: a key split across two pieces is still counted once. Text inside string
/// values never counts (quotes there are escaped).
/// </summary>
public sealed class StudySetItemCounter : IAiOutputObserver
{
    private const string ItemEndKey = "page";

    private readonly char[] _string = new char[ItemEndKey.Length + 1];
    private int _stringLength;
    private bool _inString;
    private bool _escaped;
    private bool _stringWasItemEndKey;
    private bool _awaitingColon;

    public int Count { get; private set; }

    /// <summary>Called whenever Count grows.</summary>
    public event Action<int>? Changed;

    public void Restart()
    {
        _stringLength = 0;
        _inString = _escaped = _stringWasItemEndKey = _awaitingColon = false;
        if (Count == 0) return;
        Count = 0;
        Changed?.Invoke(0);
    }

    public void Append(string text)
    {
        var before = Count;
        foreach (var c in text)
        {
            if (_inString)
            {
                if (_escaped)
                {
                    _escaped = false;
                    Keep(c);
                }
                else if (c == '\\')
                {
                    _escaped = true;
                    Keep(c);
                }
                else if (c == '"')
                {
                    _inString = false;
                    _stringWasItemEndKey = _stringLength == ItemEndKey.Length && new string(_string, 0, _stringLength) == ItemEndKey;
                    _awaitingColon = true;
                }
                else
                {
                    Keep(c);
                }
            }
            else if (c == '"')
            {
                _inString = true;
                _stringLength = 0;
                _awaitingColon = false;
            }
            else if (_awaitingColon && !char.IsWhiteSpace(c))
            {
                // A string followed by ':' is a key.
                if (c == ':' && _stringWasItemEndKey) Count++;
                _awaitingColon = false;
            }
        }

        if (Count != before) Changed?.Invoke(Count);
    }

    /// <summary>Only the first few characters matter for comparing with "page".</summary>
    private void Keep(char c)
    {
        if (_stringLength < _string.Length) _string[_stringLength] = c;
        _stringLength++;
    }
}
