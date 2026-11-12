using Studeo.Api.Services;

namespace Studeo.Tests.Unit;

public class PageQualityTests
{
    /// <summary>A page scanned upside down and OCR'd (from a real upload).</summary>
    public const string Garbage =
        "-οrιο ΛΦτ 5quodýl3rI sξι)ιγη 5ι]r η)οτ3ßυ9ιφ_ιιιιDý^!λ !Λ Dι^ρυ Bu9dU οθ ^φrßοιτDrΙυχοDΒrΙ ΛΦιοτ?τ uoor?Ξ. " +
        ",ýλσdDΕ ΛιüΞτΓρληg ^Φr òß)ηΙυρΛD$üdorlsιr Þιγ=ι,ιιdι gu ο ,ιοο ηο^dηοlι3γ 53]οι]ο sιτο οο?τΙsο]ουDοιgι sι3ο?χο " +
        "ιλ3λ Λο1 ι?)ιdοθD)ι uco) òι)ιγη Λυ]ο òυλΦ ,DdDU Η ,Sηουο)ο 'ηοΛιο)l ιο)] οι3χιοιο 9^ιο) DΛ?rjoιdo Λη 9d31 υ 'üΦο " +
        "Uduyr9yo òυοgθ sq}ιτΙοΛοrιο òυ] qλογγο τ]Γþ ßοl^üU?dioι3rI sι3o3xo gιonodDu s ^υ1 ηlο),DlD]ηχο] ηγου,ολ!γ " +
        "ιοl3γγDθοýτΙ !n?}oDlo)d3!n οιý Λυ1 3ι^ι,5υοDτο9^0ι]3 5q)ιΛΦ^ιο) sur03! υ ιDΒχd?υ3 3rg1ιο ηιηο sgλ sιl:ιi";

    /// <summary>Readable, though the OCR swapped letters («τns», «κα1»): from the same upload.</summary>
    public const string NoisyButReadable =
        "κο1νων1κό γεγονόs κα1 ωs εκ τούτου n 1δ1ωηκn ωφέλψn εργασiα κάθε παραγωγού κατατ:άσσετα� 1σότ:ψα μαζi με αυτέs " +
        "των υπολοiπων. Η εξiσωσn «toto coelo» (απ' όλεs τιs απόψε1s) των π10 δ�αφορετ:�κών μορφών εργασfαs μπορεi να " +
        "υπάρχε1 μόνο στnν αφαiρεσn από τnν πραγματικn τουs αν1σότ:nτα n ανάγοντ:άs τα στο κο1νό χαρακτ:nρ1- στ1κό τουs, " +
        "δnλαδn τ:n δαπάνn τ:ns ανθρώπ1νns εQγατ:1κns δύναμns n, π10 αφnρnμένα, τ:ns ανθρώπ1νns εργασiαs. Ο δ1πλόs " +
        "κο1νων1κόs χαρακτnραs τ:ns εργασiαs του ατόμου εμφανiζετα1 σε αυτό όταν αντανακλάτα1 στον νου του μόνο";

    private const string CleanGreek =
        "Η φωτοσύνθεση είναι η διαδικασία με την οποία τα φυτά μετατρέπουν την ηλιακή ενέργεια σε χημική ενέργεια. " +
        "Γίνεται στους χλωροπλάστες των κυττάρων του φύλλου και χρειάζεται νερό, διοξείδιο του άνθρακα και φως. " +
        "Από τη διαδικασία αυτή παράγεται γλυκόζη και ελευθερώνεται οξυγόνο στην ατμόσφαιρα, που είναι απαραίτητο " +
        "για την αναπνοή των ζώων και του ανθρώπου.";

    [Fact]
    public void A_garbage_scan_page_is_unreadable() => Assert.True(PageQuality.IsUnreadable(Garbage));

    [Theory]
    [InlineData(NoisyButReadable)]
    [InlineData(CleanGreek)]
    public void Readable_text_is_not_flagged(string text) => Assert.False(PageQuality.IsUnreadable(text));

    [Fact]
    public void English_prose_is_not_flagged() =>
        Assert.False(PageQuality.IsUnreadable(string.Join(' ', Enumerable.Repeat(TestPdfs.LongSentence, 8))));

    [Fact]
    public void Clean_text_with_few_common_words_is_not_flagged()
    {
        // A CV or a slide: names, terms and dates, hardly any «και» or «του».
        var text = string.Join(' ', Enumerable.Repeat("Εκπαίδευση Πανεπιστήμιο Αθηνών Θεατρολογία Μεταπτυχιακό Παιδαγωγική", 10));

        Assert.False(PageQuality.IsUnreadable(text));
    }

    [Fact]
    public void A_short_page_is_not_judged() => Assert.False(PageQuality.IsUnreadable("^ι]Ι οιλ ^Do ο1 urιloD u^3TiooonlUD^o"));

    [Fact]
    public void Lists_the_unreadable_page_numbers() =>
        Assert.Equal([2, 4], PageQuality.UnreadablePages([CleanGreek, Garbage, NoisyButReadable, Garbage, ""]));
}
