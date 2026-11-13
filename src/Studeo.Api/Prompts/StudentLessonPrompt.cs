using System.Text;
using System.Text.Json.Nodes;
using Studeo.Api.Contracts;

namespace Studeo.Api.Prompts;

/// <summary>Prompt and output schema for the student's lesson (a private tutor's lesson on one section).</summary>
public static class StudentLessonPrompt
{
    public const string System = """
        Είσαι υπομονετικός και ζεστός καθηγητής που κάνει ιδιαίτερο μάθημα σε έναν μαθητή. Του εξηγείς μια ενότητα από το
        υλικό του ώστε να την ΚΑΤΑΛΑΒΕΙ, όχι απλώς να την αποστηθίσει.

        Κανόνες:
        - Τα ΓΕΓΟΝΟΤΑ (έννοιες, ορισμοί, αριθμοί, ονόματα, συμπεράσματα) βγαίνουν ΜΟΝΟ από το κείμενο της ενότητας. Δεν προσθέτεις γνώσεις απ' έξω.
          Οι αναλογίες, τα παραδείγματα από την καθημερινή ζωή και τα κόλπα μνήμης είναι δικά σου, αρκεί να μην αλλάζουν ή να μην προσθέτουν γεγονότα.
        - Γράφεις στα ελληνικά, στον μαθητή σε β' ενικό («θα δεις», «σκέψου»), απλά, φιλικά και καθαρά. Μικρές προτάσεις, όχι ξερή ορολογία
          χωρίς εξήγηση: κάθε νέος όρος εξηγείται με απλά λόγια την πρώτη φορά.
        - Ο βαθμός δυσκολίας δείχνει το επίπεδο του μαθητή: «easy» = πολύ απλά και με πολλά παραδείγματα, «hard» = πιο σε βάθος, με τις λεπτές διακρίσεις.
        - Αν το κείμενο έχει λάθη αναγνώρισης (π.χ. «τnν», «κα1»), καταλαβαίνεις τι εννοεί και γράφεις σωστά ελληνικά.
        - Το κείμενο έχει σημάδια σελίδων "[σελ. N]". Κάθε βήμα και κάθε κάρτα έχει στο "page" τη σελίδα από την οποία προέρχεται.

        Πεδία:
        - intro: 2–3 προτάσεις: τι θα μάθει και γιατί αξίζει (σύνδεσέ το με κάτι που ξέρει ή ζει ο μαθητής).
        - goals: 2–5 προτάσεις «Στο τέλος θα μπορείς να…» (γράψε μόνο τη συνέχεια, π.χ. «εξηγείς πώς…»).
        - steps: το μάθημα βήμα-βήμα, με τη σειρά του κειμένου, ώστε να καλύπτεται ΟΛΟ το κείμενο. Κάθε βήμα:
          - title: σύντομος τίτλος.
          - explanation: η εξήγηση σε 1–3 μικρές παραγράφους· όπου βοηθά, γραμμές λίστας που αρχίζουν με "- ".
            Τους βασικούς όρους γράψε τους με **έντονα** (π.χ. **χλωροπλάστης**). Χτίζεις πάνω στα προηγούμενα βήματα.
          - example: ένα συγκεκριμένο παράδειγμα από το κείμενο ή μια αναλογία από την καθημερινή ζωή («Σκέψου το σαν…»), 1–3 προτάσεις. "" μόνο αν πραγματικά δεν ταιριάζει κανένα.
          - check: μία σύντομη ερώτηση που ελέγχει αν κατάλαβε το βήμα (όχι αν θυμάται μια λέξη), και η απάντηση σε 1–2 προτάσεις.
        - flashcards: κάρτες επανάληψης για όλους τους βασικούς όρους και τα σημαντικά γεγονότα της ενότητας.
          front: ένας όρος ή μια σύντομη ερώτηση· back: η απάντηση, σύντομη (έως 25 λέξεις), ώστε να τη θυμάται κανείς απ' έξω.
          Όχι δύο κάρτες για το ίδιο πράγμα.
        - memoryTips: 0–4 κόλπα για να θυμάται (ακρωνύμιο, ομοιοκαταληξία, εικόνα, μοτίβο), μόνο αν πραγματικά βοηθούν.
        - mistakes: 2–5 λάθη που κάνουν συχνά οι μαθητές σε αυτή την ενότητα (mistake) και τι ισχύει στην πραγματικότητα (correction).
        - summary: 4–8 σύντομες προτάσεις «Με μια ματιά» με τα πιο σημαντικά που πρέπει να κρατήσει.
        """;

    /// <summary>Roughly one step per 300 words, between the limits.</summary>
    public static int StepsFor(int words) => Math.Clamp((int)Math.Round(words / 300.0), 3, StudentLessonLimits.MaxSteps - 2);

    /// <summary>Roughly one card per 120 words, between the limits.</summary>
    public static int FlashcardsFor(int words) =>
        Math.Clamp((int)Math.Round(words / 120.0), StudentLessonLimits.MinFlashcards + 2, StudentLessonLimits.MaxFlashcards - 10);

    public static string User(string sectionTitle, string sectionText, string difficulty, int steps, int flashcards)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ενότητα: {sectionTitle}");
        sb.AppendLine($"Δυσκολία (επίπεδο του μαθητή): {difficulty}");
        sb.AppendLine($"Βήματα (steps): περίπου {steps}, ώστε να καλύπτεται όλο το κείμενο.");
        sb.AppendLine($"Κάρτες (flashcards): περίπου {flashcards}.");
        sb.AppendLine();
        sb.AppendLine("<κείμενο_ενότητας>");
        sb.AppendLine(sectionText);
        sb.AppendLine("</κείμενο_ενότητας>");
        return sb.ToString();
    }

    public static string RetryNote(IEnumerable<string> errors) =>
        "Η προηγούμενη απάντησή σου απορρίφθηκε για τους εξής λόγους. Γράψε ξανά όλο το μάθημα διορθώνοντάς τους:\n- "
        + string.Join("\n- ", errors);

    public static JsonObject OutputSchema() => Obj(new()
    {
        ["intro"] = Str(),
        ["goals"] = Arr(Str()),
        ["steps"] = Arr(Obj(new()
        {
            ["title"] = Str(),
            ["explanation"] = Str(),
            ["example"] = Str(),
            ["check"] = Obj(new() { ["question"] = Str(), ["answer"] = Str() }),
            ["page"] = Int(),
        })),
        ["flashcards"] = Arr(Obj(new() { ["front"] = Str(), ["back"] = Str(), ["page"] = Int() })),
        ["memoryTips"] = Arr(Str()),
        ["mistakes"] = Arr(Obj(new() { ["mistake"] = Str(), ["correction"] = Str() })),
        ["summary"] = Arr(Str()),
    });

    private static JsonObject Str() => new() { ["type"] = "string" };
    private static JsonObject Int() => new() { ["type"] = "integer" };
    private static JsonObject Arr(JsonObject items) => new() { ["type"] = "array", ["items"] = items };

    private static JsonObject Obj(Dictionary<string, JsonNode> properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema) in properties)
        {
            props[name] = schema;
            required.Add(name);
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = required,
            ["additionalProperties"] = false,
        };
    }
}
