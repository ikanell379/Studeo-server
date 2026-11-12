using System.Text;
using System.Text.Json.Nodes;
using Studeo.Api.Contracts;

namespace Studeo.Api.Prompts;

/// <summary>Prompt and output schema for generating a StudySet from one section (SPEC: "Οδηγίες για το prompt παραγωγής").</summary>
public static class GenerateStudySetPrompt
{
    public const string System = """
        Είσαι έμπειρος καθηγητής που φτιάχνει υλικό επανάληψης και αξιολόγησης για εξετάσεις, για μαθητές και φοιτητές.

        Κανόνες:
        - Βασίζεσαι ΜΟΝΟ στο κείμενο της ενότητας που σου δίνεται. Δεν προσθέτεις καμία πληροφορία απ' έξω, ακόμη κι αν τη γνωρίζεις.
        - Εστιάζεις στα πιο σημαντικά σημεία: ορισμούς, βασικές έννοιες, σχέσεις, αιτίες και αποτελέσματα, αριθμούς και ημερομηνίες που μετράνε.
        - Τηρείς ΑΚΡΙΒΩΣ τις ποσότητες που ζητούνται για κάθε τύπο. Αν μια ποσότητα είναι 0, η αντίστοιχη λίστα είναι κενή.
        - Γράφεις στα ελληνικά, με σωστή ορολογία. Οι ασκήσεις έχουν διατύπωση όπως σε σχολικές και πανεπιστημιακές εξετάσεις.
        - Αν το κείμενο έχει λάθη αναγνώρισης (π.χ. «τnν», «κα1», «τns» αντί για «την», «και», «της»), καταλαβαίνεις τι εννοεί και γράφεις πάντα σωστά ελληνικά.
        - Το κείμενο έχει σημάδια σελίδων της μορφής "[σελ. N]". Κάθε στοιχείο έχει στο πεδίο "page" τη σελίδα από όπου προέρχεται κυρίως.

        Θεωρία (theory): ο σκοπός της είναι να ΜΑΘΕΙ κάποιος την ενότητα εύκολα, σαν καλές σημειώσεις ενός καθηγητή. Δεν είναι περίληψη ούτε αντιγραφή του κειμένου.
        - concept: σύντομος, σαφής τίτλος της έννοιας (π.χ. «Πρωτόγονη συσσώρευση του κεφαλαίου»).
        - explanation: ΑΠΛΟΠΟΙΕΙΣ. Ξεκινάς με 1–2 απλές προτάσεις, σε καθημερινή γλώσσα, που λένε τι είναι και γιατί έχει σημασία.
          Μετά, όπου η έννοια έχει πολλά σημεία (χαρακτηριστικά, αιτίες, συνέπειες, στάδια, διαφορές), τα γράφεις ως κουκκίδες:
          κάθε κουκκίδα σε δική της γραμμή, που αρχίζει με "- ", μία σύντομη ιδέα η καθεμία (3–6 κουκκίδες).
          Μια απλή έννοια μένει σε 2–3 προτάσεις χωρίς κουκκίδες.
        - Όταν βοηθά, προσθέτεις στο τέλος μια γραμμή «Παράδειγμα: …» με παράδειγμα ΑΠΟ ΤΟ ΚΕΙΜΕΝΟ.
        - Τους δύσκολους όρους τους εξηγείς με απλά λόγια την πρώτη φορά που εμφανίζονται. Βάζεις με έντονα (**όρος**) τους λίγους βασικούς όρους που πρέπει να θυμάται.
        - Κάθε στοιχείο της θεωρίας διαβάζεται μόνο του, χωρίς το υπόλοιπο κείμενο. Δεν προσθέτεις γεγονότα που δεν υπάρχουν στο κείμενο. Η απλοποίηση αφορά μόνο τη διατύπωση.

        Δυσκολία:
        - easy (εύκολο): αναγνώριση και ορισμοί.
        - medium (μέτριο): κατανόηση και σύνδεση εννοιών.
        - hard (δύσκολο): εφαρμογή, σύγκριση, λεπτές διακρίσεις.

        Τύποι:
        - theory: έννοια (concept) και απλοποιημένη εξήγηση με κουκκίδες όπου χρειάζεται (explanation), όπως περιγράφεται παραπάνω.
        - openQuestions: ερώτηση ανάπτυξης και πλήρης, σύντομη πρότυπη απάντηση.
        - trueFalse: πρόταση, αν είναι σωστή (isTrue) και αιτιολόγηση. Περίπου οι μισές να είναι λάθος. Οι λάθος προτάσεις να είναι αληθοφανείς, όχι προφανείς.
        - matching: οδηγία και 4–6 ζεύγη (left → right). Όλα τα left διαφορετικά μεταξύ τους, και όλα τα right διαφορετικά μεταξύ τους. Τα ζεύγη δίνονται στη σωστή αντιστοίχιση.
        - multipleChoice: ερώτηση, ακριβώς 4 διαφορετικές επιλογές, ο δείκτης της σωστής (correctIndex, 0–3) και εξήγηση. Οι λάθος επιλογές να είναι αληθοφανείς. Μοίρασε τη σωστή απάντηση σε διάφορες θέσεις.
        - fillBlanks: πρόταση με ακριβώς ένα κενό, σημειωμένο ως "____" (τέσσερις κάτω παύλες), και η λέξη ή φράση που λείπει (answer).
        """;

    /// <summary>Extra instructions for "full" mode: cover the whole section, in order.</summary>
    public const string FullCoverage = """
        Λειτουργία ΠΛΗΡΟΥΣ ΚΑΛΥΨΗΣ: το υλικό πρέπει να καλύπτει ΟΛΟ το κείμενο της ενότητας, όχι μόνο τα πιο σημαντικά σημεία.
        - Πέρασε το κείμενο με τη σειρά, από την αρχή ως το τέλος, και μοίρασε τα στοιχεία σε όλες τις σελίδες του.
        - Κάθε έννοια, ορισμός, γεγονός, σχέση, αριθμός ή ημερομηνία του κειμένου εμφανίζεται στη θεωρία και ελέγχεται από τουλάχιστον μία άσκηση.
        - Η θεωρία ακολουθεί τη σειρά του κειμένου, ώστε να διαβάζεται σαν πλήρης επανάληψη της ενότητας.
        - Μην επαναλαμβάνεις το ίδιο σημείο σε πολλές ασκήσεις του ίδιου τύπου, όσο υπάρχουν σημεία που δεν έχουν ελεγχθεί.
        """;

    /// <summary>For one of several parallel calls: only some kinds of items are asked for here.</summary>
    public const string PartOnly = """
        Σε αυτό το αίτημα φτιάχνεις ΜΟΝΟ τους τύπους με ποσότητα μεγαλύτερη από 0. Οι υπόλοιποι τύποι φτιάχνονται χωριστά,
        οπότε οι λίστες τους μένουν κενές. Δώσε όλη σου την προσοχή στους τύπους που ζητούνται.
        """;

    public static string User(
        string sectionTitle, string sectionText, string difficulty, QuestionCounts counts, bool fullCoverage = false, bool partOnly = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ενότητα: {sectionTitle}");
        sb.AppendLine($"Δυσκολία: {difficulty}");
        if (fullCoverage)
        {
            sb.AppendLine();
            sb.AppendLine(FullCoverage);
        }
        if (partOnly)
        {
            sb.AppendLine();
            sb.AppendLine(PartOnly);
        }
        sb.AppendLine("Ποσότητες:");
        sb.AppendLine($"- theory: {counts.Theory}");
        sb.AppendLine($"- openQuestions: {counts.OpenQuestions}");
        sb.AppendLine($"- trueFalse: {counts.TrueFalse}");
        sb.AppendLine($"- matching: {counts.Matching}");
        sb.AppendLine($"- multipleChoice: {counts.MultipleChoice}");
        sb.AppendLine($"- fillBlanks: {counts.FillBlanks}");
        sb.AppendLine();
        sb.AppendLine("<κείμενο_ενότητας>");
        sb.AppendLine(sectionText);
        sb.AppendLine("</κείμενο_ενότητας>");
        return sb.ToString();
    }

    /// <summary>Added to the user message on the single retry after a failed validation.</summary>
    public static string RetryNote(IEnumerable<string> errors) =>
        "Η προηγούμενη απάντησή σου απορρίφθηκε για τους εξής λόγους. Φτιάξε ξανά όλο το υλικό διορθώνοντάς τους:\n- "
        + string.Join("\n- ", errors);

    /// <summary>JSON Schema for structured output: the StudySet shape without ids (the server assigns them).</summary>
    public static JsonObject OutputSchema() => Obj(new()
    {
        ["sectionTitle"] = Str(),
        ["difficulty"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("easy", "medium", "hard") },
        ["theory"] = Arr(Obj(new() { ["concept"] = Str(), ["explanation"] = Str(), ["page"] = Int() })),
        ["openQuestions"] = Arr(Obj(new() { ["question"] = Str(), ["answer"] = Str(), ["page"] = Int() })),
        ["trueFalse"] = Arr(Obj(new()
        {
            ["statement"] = Str(), ["isTrue"] = new JsonObject { ["type"] = "boolean" }, ["justification"] = Str(), ["page"] = Int(),
        })),
        ["matching"] = Arr(Obj(new()
        {
            ["instructions"] = Str(),
            ["pairs"] = Arr(Obj(new() { ["left"] = Str(), ["right"] = Str() })),
            ["page"] = Int(),
        })),
        ["multipleChoice"] = Arr(Obj(new()
        {
            ["question"] = Str(), ["options"] = Arr(Str()), ["correctIndex"] = Int(), ["explanation"] = Str(), ["page"] = Int(),
        })),
        ["fillBlanks"] = Arr(Obj(new() { ["sentence"] = Str(), ["answer"] = Str(), ["page"] = Int() })),
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
