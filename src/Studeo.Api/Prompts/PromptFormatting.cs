using System.Text;
using Studeo.Api.Contracts;

namespace Studeo.Api.Prompts;

/// <summary>
/// What all prompts share about formulas and code: LaTeX between $ (KaTeX in the UI, mhchem for chemistry) and
/// Markdown code fences, and a note on the kind of material (science / programming) in the user message.
/// </summary>
public static class PromptFormatting
{
    /// <summary>Appended to every system prompt.</summary>
    public const string Rules = """

        Τύποι και κώδικας (όπου το υλικό έχει):
        - Μαθηματικά και φυσική σε LaTeX: μέσα στην πρόταση ανάμεσα σε $…$ (π.χ. $v = \frac{\Delta x}{\Delta t}$), ένας μεγάλος τύπος
          σε δική του γραμμή ανάμεσα σε $$…$$. Μονάδες μέσα στον τύπο με \text{} (π.χ. $20\,\text{m/s}$). Μικρά σύμβολα μεγεθών επίσης σε $…$ (π.χ. $F$, $\lambda$).
        - Χημικοί τύποι και αντιδράσεις με \ce{} μέσα σε $…$: $\ce{H2SO4}$, $\ce{2H2 + O2 -> 2H2O}$, $\ce{N2 + 3H2 <=> 2NH3}$.
        - Κώδικας σε μπλοκ: μια γραμμή ```γλώσσα (π.χ. ```python), ο κώδικας με τη σωστή στοίχιση, και μια γραμμή ```. Ονόματα
          μεταβλητών, συναρτήσεων και εντολών μέσα σε πρόταση ανάμεσα σε `…` (π.χ. η `for` επαναλαμβάνει…).
        - Στο κείμενο της ενότητας οι τύποι είναι ήδη σε LaTeX ($…$) και ο κώδικας σε ```, αν βγήκαν σωστά. Αν ένας τύπος χάλασε στην
          εξαγωγή από το αρχείο (π.χ. «x2» για x², «H2O» χωρίς δείκτη, κλάσμα σε δύο γραμμές), τον γράφεις σωστά όταν το νόημα είναι σαφές.
        - Το σύμβολο $ μόνο για τύπους: για χρήματα γράψε «δολάρια» ή «ευρώ». Μη βάζεις τύπους ή κώδικα σε υλικό χωρίς τέτοια.
        """;

    /// <summary>The note on the kind of material for the user message ("" for general material).</summary>
    public static string SubjectNote(MaterialSubject? subject)
    {
        if (subject is null || subject.Subject == Subjects.General)
        {
            return "";
        }
        var sb = new StringBuilder();
        if (subject.Subject == Subjects.Science)
        {
            sb.AppendLine("Είδος υλικού: θετικές επιστήμες (μαθηματικά, φυσική, χημεία). Κάθε τύπος, μέγεθος με μονάδα και χημική");
            sb.AppendLine("εξίσωση γράφεται σε LaTeX όπως παραπάνω. Όπου βοηθά, δείξε πώς εφαρμόζεται ένας τύπος με αριθμούς από το κείμενο.");
        }
        else
        {
            var language = LanguageName(subject.Language);
            sb.AppendLine($"Είδος υλικού: προγραμματισμός{(language is null ? "" : $" (γλώσσα: {language})")}. Κάθε κομμάτι κώδικα σε μπλοκ");
            sb.AppendLine($"```{subject.Language ?? "γλώσσα"} … ``` με σωστή στοίχιση, γραμμένο στη γλώσσα του κειμένου. Όπου βοηθά, δείξε σύντομο κώδικα και τι κάνει.");
            if (subject.Language == ProgrammingLanguages.Glossa)
            {
                sb.AppendLine("Η ΓΛΩΣΣΑ είναι η ψευδογλώσσα του μαθήματος ΑΕΠΠ: γράψε τις εντολές της με κεφαλαία (ΑΝ … ΤΟΤΕ, ΓΙΑ … ΑΠΟ … ΜΕΧΡΙ, ΓΡΑΨΕ) και ανάθεση με <-.");
            }
        }
        return sb.ToString();
    }

    private static string? LanguageName(string? language) => language switch
    {
        "python" => "Python",
        "c" => "C",
        "cpp" => "C++",
        "java" => "Java",
        "csharp" => "C#",
        "javascript" => "JavaScript",
        "sql" => "SQL",
        ProgrammingLanguages.Glossa => "ΓΛΩΣΣΑ (ΑΕΠΠ)",
        _ => null,
    };
}
