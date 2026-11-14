using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;

namespace Studeo.Api.Services;

/// <summary>
/// An Office Math (OMML) equation from Word or PowerPoint written as LaTeX, so the AI reads the formula and the UI
/// shows it with KaTeX. Read by element name only, so it works on the typed elements of a .docx and on the
/// unrecognised ones inside a PowerPoint «a14:m». Covers what school and university material uses: fractions,
/// powers and indices, roots, brackets, sums and integrals, functions, limits, accents, bars, matrices and
/// equation arrays; anything else keeps its text.
/// </summary>
public static partial class OmmlLatex
{
    /// <summary>The LaTeX of an «m:oMath» or «m:oMathPara» (without $ delimiters).</summary>
    public static string Convert(OpenXmlElement math) => Tidy(math.LocalName == "oMathPara"
        ? string.Join(@" \\ ", math.ChildElements.Where(c => c.LocalName == "oMath").Select(Children))
        : Children(math));

    /// <summary>The first «oMath»/«oMathPara» inside an element (e.g. PowerPoint's «a14:m»), or null.</summary>
    public static OpenXmlElement? FindMath(OpenXmlElement element)
    {
        if (element.Descendants().FirstOrDefault(e => e.LocalName is "oMathPara" or "oMath") is { } math)
        {
            return math;
        }

        // PowerPoint's «a14:m» is not an element the SDK knows: its equation stays unparsed XML inside it.
        var holder = element.Descendants().Prepend(element)
            .FirstOrDefault(e => e.LocalName == "m" && !e.HasChildren && e.InnerXml.Contains("oMath", StringComparison.Ordinal));
        return holder is null ? null : Parse(holder.InnerXml.Trim());
    }

    /// <summary>
    /// The equation from raw XML. Its prefixes were declared further up in the file, so they are declared again on it.
    /// Null when it can't be read.
    /// </summary>
    private static OpenXmlElement? Parse(string xml)
    {
        var tagEnd = xml.IndexOfAny([' ', '>', '/']);
        if (!xml.StartsWith("<m:oMath", StringComparison.Ordinal) || tagEnd < 0)
        {
            return null;
        }
        var declarations = string.Concat(Namespaces
            .Where(ns => !xml.Contains($"xmlns:{ns.Prefix}=", StringComparison.Ordinal))
            .Select(ns => $" xmlns:{ns.Prefix}=\"{ns.Uri}\""));
        xml = xml[..tagEnd] + declarations + xml[tagEnd..];
        try
        {
            return xml.StartsWith("<m:oMathPara", StringComparison.Ordinal)
                ? new DocumentFormat.OpenXml.Math.Paragraph(xml)
                : new DocumentFormat.OpenXml.Math.OfficeMath(xml);
        }
        catch (Exception)
        {
            return null; // an equation we can't read is left out, like before
        }
    }

    private static readonly (string Prefix, string Uri)[] Namespaces =
    [
        ("m", "http://schemas.openxmlformats.org/officeDocument/2006/math"),
        ("a", "http://schemas.openxmlformats.org/drawingml/2006/main"),
        ("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main"),
        ("a14", "http://schemas.microsoft.com/office/drawing/2010/main"),
        ("mc", "http://schemas.openxmlformats.org/markup-compatibility/2006"),
    ];

    private static string Children(OpenXmlElement? e) => e is null ? "" : string.Concat(e.ChildElements.Select(Node));

    private static string Node(OpenXmlElement e) => e.LocalName switch
    {
        "r" => Run(e),
        "f" => Fraction(e),
        "sSup" => $"{Group(Arg(e, "e"))}^{{{Children(Arg(e, "sup"))}}}",
        "sSub" => $"{Group(Arg(e, "e"))}_{{{Children(Arg(e, "sub"))}}}",
        "sSubSup" => $"{Group(Arg(e, "e"))}_{{{Children(Arg(e, "sub"))}}}^{{{Children(Arg(e, "sup"))}}}",
        "sPre" => $"{{}}_{{{Children(Arg(e, "sub"))}}}^{{{Children(Arg(e, "sup"))}}}{Group(Arg(e, "e"))}",
        "rad" => Radical(e),
        "d" => Delimiter(e),
        "nary" => Nary(e),
        "func" => $"{Children(Arg(e, "fName"))} {Group(Arg(e, "e"))}",
        "acc" => $"{AccentCommand(Prop(e, "accPr", "chr") ?? "̂")}{{{Children(Arg(e, "e"))}}}",
        "bar" => Prop(e, "barPr", "pos") == "top" ? $@"\overline{{{Children(Arg(e, "e"))}}}" : $@"\underline{{{Children(Arg(e, "e"))}}}",
        "limLow" => $"{Children(Arg(e, "e"))}_{{{Children(Arg(e, "lim"))}}}",
        "limUpp" => $"{Children(Arg(e, "e"))}^{{{Children(Arg(e, "lim"))}}}",
        "groupChr" => Prop(e, "groupChrPr", "pos") == "top"
            ? $@"\overbrace{{{Children(Arg(e, "e"))}}}"
            : $@"\underbrace{{{Children(Arg(e, "e"))}}}",
        "m" => Matrix(e),
        "eqArr" => $@"\begin{{gathered}}{string.Join(@" \\ ", e.ChildElements.Where(c => c.LocalName == "e").Select(Children))}\end{{gathered}}",
        "oMath" => Children(e),
        // Properties (fPr, ctrlPr, rPr…) carry no text.
        _ when e.LocalName.EndsWith("Pr", StringComparison.Ordinal) => "",
        // e, num, den, box, borderBox, phant… and anything unknown: just their content.
        _ => Children(e),
    };

    private static OpenXmlElement? Arg(OpenXmlElement e, string name) =>
        e.ChildElements.FirstOrDefault(c => c.LocalName == name);

    private static string Group(OpenXmlElement? e) => $"{{{Children(e)}}}";

    /// <summary>The «m:val» of a property: e.g. Prop(d, "dPr", "begChr"). Null when not set.</summary>
    private static string? Prop(OpenXmlElement e, string properties, string name) =>
        Arg(e, properties) is { } pr && Arg(pr, name) is { } p
            ? p.GetAttributes().FirstOrDefault(a => a.LocalName == "val").Value ?? "on"
            : null;

    private static bool On(string? value) => value is "1" or "on" or "true";

    private static string Fraction(OpenXmlElement e)
    {
        var num = Children(Arg(e, "num"));
        var den = Children(Arg(e, "den"));
        return Prop(e, "fPr", "type") switch
        {
            "lin" or "skw" => $"{{{num}}}/{{{den}}}",
            "noBar" => $@"\genfrac{{}}{{}}{{0pt}}{{}}{{{num}}}{{{den}}}",
            _ => $@"\frac{{{num}}}{{{den}}}",
        };
    }

    private static string Radical(OpenXmlElement e)
    {
        var degree = Children(Arg(e, "deg"));
        var body = Children(Arg(e, "e"));
        return On(Prop(e, "radPr", "degHide")) || degree.Trim().Length == 0
            ? $@"\sqrt{{{body}}}"
            : $@"\sqrt[{degree}]{{{body}}}";
    }

    private static string Delimiter(OpenXmlElement e)
    {
        var begin = Prop(e, "dPr", "begChr") ?? "(";
        var end = Prop(e, "dPr", "endChr") ?? ")";
        var separator = Prop(e, "dPr", "sepChr") ?? "|";
        var parts = e.ChildElements.Where(c => c.LocalName == "e").Select(Children);
        var sep = separator == "|" ? @" \mid " : $" {Symbols(separator)} ";
        return $@"\left{Fence(begin)} {string.Join(sep, parts)} \right{Fence(end)}";
    }

    private static string Fence(string c) => c switch
    {
        "" => ".",
        "{" => @"\{",
        "}" => @"\}",
        "‖" => @"\|",
        "⟨" or "〈" => @"\langle",
        "⟩" or "〉" => @"\rangle",
        "⌊" => @"\lfloor",
        "⌋" => @"\rfloor",
        "⌈" => @"\lceil",
        "⌉" => @"\rceil",
        "[" or "]" or "(" or ")" or "|" or "/" => c,
        _ => ".",
    };

    private static string Nary(OpenXmlElement e)
    {
        var command = (Prop(e, "naryPr", "chr") ?? "∫") switch
        {
            "∑" => @"\sum",
            "∏" => @"\prod",
            "∐" => @"\coprod",
            "∬" => @"\iint",
            "∭" => @"\iiint",
            "∮" => @"\oint",
            "⋃" => @"\bigcup",
            "⋂" => @"\bigcap",
            "⋁" => @"\bigvee",
            "⋀" => @"\bigwedge",
            _ => @"\int",
        };
        var sub = On(Prop(e, "naryPr", "subHide")) ? "" : Children(Arg(e, "sub"));
        var sup = On(Prop(e, "naryPr", "supHide")) ? "" : Children(Arg(e, "sup"));
        return command
            + (sub.Trim().Length > 0 ? $"_{{{sub}}}" : "")
            + (sup.Trim().Length > 0 ? $"^{{{sup}}}" : "")
            + $" {Group(Arg(e, "e"))}";
    }

    private static string AccentCommand(string c) => c switch
    {
        "̃" or "~" => @"\tilde",
        "̇" or "˙" => @"\dot",
        "̈" or "¨" => @"\ddot",
        "⃗" or "→" or "⃑" => @"\vec",
        "̄" or "̅" or "¯" => @"\bar",
        "̌" => @"\check",
        "̆" => @"\breve",
        "⃖" or "←" => @"\overleftarrow",
        _ => @"\hat",
    };

    private static string Matrix(OpenXmlElement e)
    {
        var rows = e.ChildElements.Where(c => c.LocalName == "mr")
            .Select(r => string.Join(" & ", r.ChildElements.Where(c => c.LocalName == "e").Select(Children)));
        return $@"\begin{{matrix}}{string.Join(@" \\ ", rows)}\end{{matrix}}";
    }

    /// <summary>A run of math text. «Normal text» runs (m:nor) and words of several letters become \text{…}.</summary>
    private static string Run(OpenXmlElement run)
    {
        var text = string.Concat(run.ChildElements.Where(c => c.LocalName == "t").Select(t => t.InnerText));
        if (text.Length == 0)
        {
            return "";
        }
        var normal = Arg(run, "rPr") is { } pr && Arg(pr, "nor") is not null;
        if (normal || IsWords(text))
        {
            return $@"\text{{{EscapeText(text)}}}";
        }
        return Symbols(text);
    }

    /// <summary>«όπου», «αν x > 0»: prose inside an equation (not a function name like «sin» or «lim»).</summary>
    private static bool IsWords(string text) =>
        WordRegex().Matches(text).Any(m => !Functions.Any(f => m.Value.StartsWith(f, StringComparison.Ordinal) && m.Value.Length - f.Length <= 1));

    /// <summary>Three letters of one script in a row («ημx» is a function and a variable, not a word).</summary>
    [GeneratedRegex(@"\p{IsGreekandCoptic}{3,}|[A-Za-z]{3,}")]
    private static partial Regex WordRegex();

    private static readonly HashSet<string> Functions =
    [
        "sin", "cos", "tan", "cot", "sec", "csc", "arcsin", "arccos", "arctan", "sinh", "cosh", "tanh", "coth",
        "log", "ln", "lg", "exp", "lim", "max", "min", "det", "sup", "inf", "gcd", "deg", "dim", "ker", "arg",
        "ημ", "συν", "εφ", "σφ",
    ];

    /// <summary>Function names (sin, ημ…) as commands, other letters and symbols one by one.</summary>
    internal static string Symbols(string text)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var word = FunctionAt(text, i);
            if (word is not null)
            {
                // KaTeX has a command for each Latin one (\sin, \lim…); the Greek school names are operators.
                sb.Append(char.IsAsciiLetter(word[0]) ? $@"\{word} " : $@"\operatorname{{{word}}} ");
                i += word.Length;
                continue;
            }
            sb.Append(Symbol(text[i]));
            i++;
        }
        return sb.ToString();
    }

    /// <summary>A function name starting at i that is not part of a longer word («sin» but not «single»).</summary>
    private static string? FunctionAt(string text, int i)
    {
        if (i > 0 && char.IsLetter(text[i - 1]))
        {
            return null;
        }
        foreach (var f in Functions.OrderByDescending(f => f.Length))
        {
            // «ημx»: a Greek name may be followed straight by a Latin variable.
            if (string.CompareOrdinal(text, i, f, 0, f.Length) == 0
                && (i + f.Length == text.Length || !char.IsLetter(text[i + f.Length])
                    || (!char.IsAsciiLetter(f[0]) && char.IsAsciiLetter(text[i + f.Length]))))
            {
                return f;
            }
        }
        return null;
    }

    private static string Symbol(char c) => c switch
    {
        'α' => @"\alpha ", 'β' => @"\beta ", 'γ' => @"\gamma ", 'δ' => @"\delta ", 'ε' => @"\varepsilon ",
        'ϵ' => @"\epsilon ", 'ζ' => @"\zeta ", 'η' => @"\eta ", 'θ' => @"\theta ", 'ϑ' => @"\vartheta ",
        'ι' => @"\iota ", 'κ' => @"\kappa ", 'λ' => @"\lambda ", 'μ' => @"\mu ", 'ν' => @"\nu ", 'ξ' => @"\xi ",
        'ο' => "o", 'π' => @"\pi ", 'ρ' => @"\rho ", 'σ' => @"\sigma ", 'ς' => @"\varsigma ", 'τ' => @"\tau ",
        'υ' => @"\upsilon ", 'φ' => @"\varphi ", 'ϕ' => @"\phi ", 'χ' => @"\chi ", 'ψ' => @"\psi ", 'ω' => @"\omega ",
        'Γ' => @"\Gamma ", 'Δ' or '∆' => @"\Delta ", 'Θ' => @"\Theta ", 'Λ' => @"\Lambda ", 'Ξ' => @"\Xi ",
        'Π' => @"\Pi ", 'Σ' => @"\Sigma ", 'Υ' => @"\Upsilon ", 'Φ' => @"\Phi ", 'Ψ' => @"\Psi ", 'Ω' or 'Ω' => @"\Omega ",
        'Α' => "A", 'Β' => "B", 'Ε' => "E", 'Ζ' => "Z", 'Η' => "H", 'Ι' => "I", 'Κ' => "K", 'Μ' => "M", 'Ν' => "N",
        'Ο' => "O", 'Ρ' => "P", 'Τ' => "T", 'Χ' => "X",
        '×' => @"\times ", '·' or '⋅' or '∙' => @"\cdot ", '÷' => @"\div ", '±' => @"\pm ", '∓' => @"\mp ",
        '−' or '–' => "-", '≤' or '⩽' => @"\le ", '≥' or '⩾' => @"\ge ", '≠' => @"\neq ", '≈' => @"\approx ",
        '≅' => @"\cong ", '≡' => @"\equiv ", '∝' => @"\propto ", '∼' => @"\sim ", '≪' => @"\ll ", '≫' => @"\gg ",
        '∞' => @"\infty ", '→' => @"\rightarrow ", '←' => @"\leftarrow ", '↔' => @"\leftrightarrow ",
        '⇒' => @"\Rightarrow ", '⇐' => @"\Leftarrow ", '⇔' => @"\Leftrightarrow ", '⇌' => @"\rightleftharpoons ",
        '↑' => @"\uparrow ", '↓' => @"\downarrow ", '∂' => @"\partial ", '∇' => @"\nabla ", '∈' => @"\in ",
        '∉' => @"\notin ", '∋' => @"\ni ", '⊂' => @"\subset ", '⊃' => @"\supset ", '⊆' => @"\subseteq ",
        '⊇' => @"\supseteq ", '∪' => @"\cup ", '∩' => @"\cap ", '∀' => @"\forall ", '∃' => @"\exists ",
        '∅' => @"\emptyset ", '¬' => @"\neg ", '∧' => @"\wedge ", '∨' => @"\vee ", '⊕' => @"\oplus ",
        '⊗' => @"\otimes ", '°' => @"^{\circ}", '′' => "'", '″' => "''", '∠' => @"\angle ", '⊥' => @"\perp ",
        '∥' => @"\parallel ", 'ℏ' => @"\hbar ", 'ℓ' => @"\ell ", '…' => @"\ldots ", '⋯' => @"\cdots ",
        '⋮' => @"\vdots ", '∑' => @"\sum ", '∏' => @"\prod ", '∫' => @"\int ", '∮' => @"\oint ", '√' => @"\surd ",
        'ℝ' => @"\mathbb{R}", 'ℕ' => @"\mathbb{N}", 'ℤ' => @"\mathbb{Z}", 'ℚ' => @"\mathbb{Q}", 'ℂ' => @"\mathbb{C}",
        '{' => @"\{", '}' => @"\}", '%' => @"\%", '#' => @"\#", '&' => @"\&", '$' => @"\$", '_' => @"\_",
        '\\' => @"\backslash ", '~' => @"\sim ", '^' => @"\hat{}", ' ' or ' ' or ' ' => " ",
        _ => c.ToString(),
    };

    private static string EscapeText(string text) =>
        text.Replace(@"\", @"\textbackslash{}").Replace("{", @"\{").Replace("}", @"\}")
            .Replace("$", @"\$").Replace("%", @"\%").Replace("&", @"\&").Replace("#", @"\#").Replace("_", @"\_");

    private static string Tidy(string latex) => SpacesRegex().Replace(latex, " ").Trim();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex SpacesRegex();
}
