using System.Text;
using System.Text.Json.Nodes;
using Studeo.Api.Contracts;

namespace Studeo.Api.Prompts;

/// <summary>Prompt and output schema for the teacher's guide on how to teach one section.</summary>
public static class TeachingGuidePrompt
{
    public const string System = """
        Είσαι έμπειρος καθηγητής και σύμβουλος διδακτικής. Βοηθάς έναν συνάδελφο να προετοιμάσει τη διδασκαλία μιας ενότητας
        από το υλικό που θα διδάξει στην τάξη του.

        Κανόνες:
        - Το ΠΕΡΙΕΧΟΜΕΝΟ (έννοιες, γεγονότα, ορισμοί, παραδείγματα) βγαίνει ΜΟΝΟ από το κείμενο της ενότητας. Δεν προσθέτεις γνώσεις απ' έξω.
          Οι διδακτικές ιδέες (πώς να το εξηγήσει, δραστηριότητες, ερωτήσεις στην τάξη) είναι δικές σου, πρακτικές και ρεαλιστικές για μια σχολική ώρα.
        - Γράφεις στα ελληνικά, απευθυνόμενος στον καθηγητή, σύντομα και χρηστικά (όχι γενικόλογες συμβουλές τύπου «κάντε το ενδιαφέρον»).
        - Αν το κείμενο έχει λάθη αναγνώρισης (π.χ. «τnν», «κα1»), καταλαβαίνεις τι εννοεί και γράφεις σωστά ελληνικά.
        - Το κείμενο έχει σημάδια σελίδων "[σελ. N]". Κάθε βασικό σημείο έχει στο "page" τη σελίδα του.

        Πεδία:
        - overview: 2–4 προτάσεις: τι πραγματεύεται η ενότητα και γιατί είναι σημαντική για τους μαθητές.
        - objectives: 3–6 διδακτικοί στόχοι, ο καθένας μια πρόταση που αρχίζει με ρήμα (π.χ. «Να εξηγούν…», «Να διακρίνουν…»).
        - prerequisites: 0–4 γνώσεις που πρέπει να έχουν ήδη οι μαθητές για να καταλάβουν την ενότητα.
        - lessonPlan: 4–6 βήματα μαθήματος με τη σειρά (αφόρμηση, παρουσίαση, εμπέδωση, ανακεφαλαίωση κ.λπ.), με τίτλο,
          λεπτά (minutes) και περιγραφή του τι κάνει ο καθηγητής και τι οι μαθητές. Σύνολο περίπου 45 λεπτά, εκτός αν η ενότητα είναι μεγάλη.
        - keyPoints: τα βασικά σημεία του κειμένου με τη σειρά που εμφανίζονται (point) και πώς να διδαχθεί το καθένα (howToTeach):
          απλή εξήγηση, αναλογία, παράδειγμα από το κείμενο, σχήμα στον πίνακα ή ερώτηση προς την τάξη.
        - misconceptions: 2–5 συνηθισμένα λάθη ή παρανοήσεις των μαθητών σε αυτή την ενότητα (mistake) και πώς να τα διορθώσει (correction).
        - discussionQuestions: 3–6 ερωτήσεις για συζήτηση στην τάξη, που ελέγχουν κατανόηση και όχι απομνημόνευση.
        """;

    /// <summary>Roughly one key point per 250 words, between the limits.</summary>
    public static int KeyPointsFor(int words) =>
        Math.Clamp((int)Math.Round(words / 250.0), TeachingGuideLimits.MinKeyPoints + 1, TeachingGuideLimits.MaxKeyPoints - 3);

    public static string User(string sectionTitle, string sectionText, string difficulty, int keyPoints)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ενότητα: {sectionTitle}");
        sb.AppendLine($"Επίπεδο τάξης/δυσκολία του τεστ που θα ακολουθήσει: {difficulty}");
        sb.AppendLine($"Βασικά σημεία (keyPoints): περίπου {keyPoints}, ώστε να καλύπτεται όλο το κείμενο.");
        sb.AppendLine();
        sb.AppendLine("<κείμενο_ενότητας>");
        sb.AppendLine(sectionText);
        sb.AppendLine("</κείμενο_ενότητας>");
        return sb.ToString();
    }

    public static string RetryNote(IEnumerable<string> errors) =>
        "Η προηγούμενη απάντησή σου απορρίφθηκε για τους εξής λόγους. Γράψε ξανά όλο τον οδηγό διορθώνοντάς τους:\n- "
        + string.Join("\n- ", errors);

    public static JsonObject OutputSchema() => Obj(new()
    {
        ["overview"] = Str(),
        ["objectives"] = Arr(Str()),
        ["prerequisites"] = Arr(Str()),
        ["lessonPlan"] = Arr(Obj(new() { ["title"] = Str(), ["minutes"] = Int(), ["description"] = Str() })),
        ["keyPoints"] = Arr(Obj(new() { ["point"] = Str(), ["howToTeach"] = Str(), ["page"] = Int() })),
        ["misconceptions"] = Arr(Obj(new() { ["mistake"] = Str(), ["correction"] = Str() })),
        ["discussionQuestions"] = Arr(Str()),
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
