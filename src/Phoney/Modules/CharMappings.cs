using System.Diagnostics.CodeAnalysis;

namespace Phoney.Modules;

/// <summary>
/// Transliteration of non-Latin letters (Cyrillic, Greek, Arabic, Farsi, Armenian, Hebrew) used to build ASCII
/// usernames. Ported from faker.js <c>src/modules/internet/char-mappings.ts</c> (v10.6.0); only single-character
/// keys are kept because faker.js maps names character by character.
/// </summary>
/// <remarks>
/// The table is one string (each pair is the letter followed by its replacement, pairs end with <c>|</c>) and is parsed the first time a
/// non-ASCII letter needs it: a table written as 281 dictionary initialisers is a 4.5 KB method that the JIT compiles in the first
/// call of every process, which is milliseconds spent on names that are almost always plain ASCII.
/// </remarks>
internal static class CharMappings
{
    private const string Pairs =
        "АA|аa|БB|бb|ВV|вv|ГG|гg|ДD|дd|ЕE|еe|ЁYo|ёyo|ЖZh|жzh|ЗZ|зz|ИI|иi|ЙY|йy|КK|кk|ЛL|лl|МM|мm|НN|нn|" +
        "ОO|оo|ПP|пp|РR|рr|СS|сs|ТT|тt|УU|уu|ФF|фf|ХKh|хkh|ЦTs|цts|ЧCh|чch|ШSh|шsh|ЩSch|щsch|Ъ|ъ|ЫY|ыy|Ь|" +
        "ь|ЭE|эe|ЮYu|юyu|ЯYa|яya|αa|βv|γg|δd|εe|ζz|ηi|θth|ιi|κk|λl|μm|νn|ξks|οo|πp|ρr|σs|τt|υy|φf|χx|ψps|" +
        "ωo|άa|έe|ίi|όo|ύy|ήi|ώo|ςs|ϊi|ΰy|ϋy|ΐi|ΑA|ΒB|ΓG|ΔD|ΕE|ΖZ|ΗI|ΘTH|ΙI|ΚK|ΛL|ΜM|ΝN|ΞKS|ΟO|ΠP|ΡR|ΣS|" +
        "ΤT|ΥY|ΦF|ΧX|ΨPS|ΩO|ΆA|ΈE|ΊI|ΌO|ΎY|ΉI|ΏO|ΪI|ΫY|ءe|آa|أa|ؤw|إi|ئy|اa|بb|ةt|تt|ثth|جj|حh|خkh|دd|" +
        "ذdh|رr|زz|سs|شsh|صs|ضd|طt|ظz|عe|غgh|ـ_|فf|قq|كk|لl|مm|نn|هh|وw|ىa|يy|ُu|چch|کk|گg|پp|ژzh|یy|աa|" +
        "ԱA|բb|ԲB|գg|ԳG|դd|ԴD|եye|ԵYe|զz|ԶZ|էe|ԷE|ըy|ԸY|թt|ԹT|ժzh|ԺZh|իi|ԻI|լl|ԼL|խkh|ԽKh|ծts|ԾTs|կk|ԿK|" +
        "հh|ՀH|ձdz|ՁDz|ղgh|ՂGh|ճtch|ՃTch|մm|ՄM|յy|ՅY|նn|ՆN|շsh|ՇSh|ոvo|ՈVo|չch|ՉCh|պp|ՊP|ջj|ՋJ|ռr|ՌR|սs|" +
        "ՍS|վv|ՎV|տt|ՏT|րr|ՐR|ցc|ՑC|փp|ՓP|քq|ՔQ|օo|ՕO|ֆf|ՖF|ևyev|אa|בb|גg|דd|הh|וv|זz|חch|טt|יy|כk|ךkh|" +
        "לl|םm|מm|ןn|נn|סs|עa|פf|ףph|צts|ץts|קk|רr|שsh|תt|";

    /// <summary>The replacement for <paramref name="letter"/>, if it has one.</summary>
    public static bool TryMap(char letter, [NotNullWhen(true)] out string? mapped) => Table.Map.TryGetValue(letter, out mapped);

    /// <summary>The number of letters with a replacement.</summary>
    public static int Count => Table.Map.Count;

    private static class Table
    {
        public static readonly Dictionary<char, string> Map = Parse();

        private static Dictionary<char, string> Parse()
        {
            var map = new Dictionary<char, string>(300);
            foreach (var pair in Pairs.Split('|', StringSplitOptions.RemoveEmptyEntries))
                map.Add(pair[0], pair[1..]);
            return map;
        }
    }
}
