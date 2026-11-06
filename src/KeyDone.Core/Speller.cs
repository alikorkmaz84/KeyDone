using System.Globalization;

namespace KeyDone.Core;

/// <summary>
/// Metni karakter karakter, ekran okuyucunun karıştırmayacağı biçimde söyler.
/// Örnek: "aB1!" → küçük a, büyük B, rakam 1, ünlem
/// </summary>
public static class Speller
{
    static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    static readonly Dictionary<string, string> Phonetic = new()
    {
        ["A"] = "Adana", ["B"] = "Bolu", ["C"] = "Ceyhan", ["Ç"] = "Çanakkale", ["D"] = "Denizli",
        ["E"] = "Edirne", ["F"] = "Fatsa", ["G"] = "Giresun", ["Ğ"] = "yumuşak ge", ["H"] = "Hatay",
        ["I"] = "Isparta", ["İ"] = "İzmir", ["J"] = "Jandarma", ["K"] = "Kars", ["L"] = "Lüleburgaz",
        ["M"] = "Muş", ["N"] = "Niğde", ["O"] = "Ordu", ["Ö"] = "Ödemiş", ["P"] = "Pazar",
        ["Q"] = "ku", ["R"] = "Rize", ["S"] = "Sinop", ["Ş"] = "Şırnak", ["T"] = "Tokat",
        ["U"] = "Uşak", ["Ü"] = "Ünye", ["V"] = "Van", ["W"] = "duble ve", ["X"] = "iks",
        ["Y"] = "Yozgat", ["Z"] = "Zonguldak",
    };

    static readonly Dictionary<string, string> Symbols = new()
    {
        [" "] = "boşluk", ["!"] = "ünlem", ["\""] = "çift tırnak", ["#"] = "diyez", ["$"] = "dolar",
        ["%"] = "yüzde", ["&"] = "ve işareti", ["'"] = "tek tırnak", ["("] = "parantez aç", [")"] = "parantez kapat",
        ["*"] = "yıldız", ["+"] = "artı", [","] = "virgül", ["-"] = "tire", ["."] = "nokta", ["/"] = "eğik çizgi",
        [":"] = "iki nokta", [";"] = "noktalı virgül", ["<"] = "küçüktür", ["="] = "eşittir", [">"] = "büyüktür",
        ["?"] = "soru işareti", ["@"] = "et işareti", ["["] = "köşeli parantez aç", ["\\"] = "ters eğik çizgi",
        ["]"] = "köşeli parantez kapat", ["^"] = "şapka", ["_"] = "alt çizgi", ["`"] = "ters tırnak",
        ["{"] = "süslü parantez aç", ["|"] = "dikey çizgi", ["}"] = "süslü parantez kapat", ["~"] = "dalga işareti",
        ["€"] = "avro", ["£"] = "sterlin", ["₺"] = "Türk lirası", ["\t"] = "sekme", ["\n"] = "yeni satır", ["\r"] = "satır başı",
    };

    public static string Describe(string ch, bool phonetic)
    {
        if (ch.Length > 0 && char.IsLetter(ch, 0))
        {
            string upper = ch.ToUpper(Turkish);
            bool isUpper = ch == upper && ch != ch.ToLower(Turkish);
            string text = $"{(isUpper ? "büyük" : "küçük")} {ch}";
            if (phonetic && Phonetic.TryGetValue(upper, out var word)) text += $", {word}";
            return text;
        }
        if (ch.Length > 0 && char.IsDigit(ch, 0)) return $"rakam {ch}";
        return Symbols.TryGetValue(ch, out var name) ? name : $"işaret {ch}";
    }

    /// <summary>Her karakter (birleşik karakterler dahil) için okunacak metin.</summary>
    public static IReadOnlyList<string> Spell(string text, bool phonetic = false)
    {
        var result = new List<string>();
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) result.Add(Describe(e.GetTextElement(), phonetic));
        return result;
    }
}
