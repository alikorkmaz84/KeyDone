using System.Security.Cryptography;

namespace KeyDone.Core;

public sealed record GeneratorOptions(
    int Length = 20,
    bool Upper = true,
    bool Lower = true,
    bool Digits = true,
    bool Symbols = true,
    bool AvoidAmbiguous = true);

public static class PasswordGenerator
{
    const string UpperSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    const string LowerSet = "abcdefghijklmnopqrstuvwxyz";
    const string DigitSet = "0123456789";
    const string SymbolSet = "!@#$%&*()-_=+[]{};:,.?/";
    const string Ambiguous = "Il1O0o|";

    public const int MinLength = 4;
    public const int MaxLength = 128;

    /// <summary>Kriptografik rastgele şifre üretir. Seçili her gruptan en az bir karakter içerir.</summary>
    public static (string Password, int Bits) Generate(GeneratorOptions o)
    {
        int length = Math.Clamp(o.Length, MinLength, MaxLength);
        var pools = new List<string>();
        if (o.Upper) pools.Add(UpperSet);
        if (o.Lower) pools.Add(LowerSet);
        if (o.Digits) pools.Add(DigitSet);
        if (o.Symbols) pools.Add(SymbolSet);
        if (o.AvoidAmbiguous)
            pools = pools.Select(p => new string(p.Where(c => !Ambiguous.Contains(c)).ToArray())).ToList();
        if (pools.Count == 0) return ("", 0);

        string all = string.Concat(pools);
        var chars = pools.Select(p => p[RandomNumberGenerator.GetInt32(p.Length)]).ToList();
        while (chars.Count < length) chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        for (int i = chars.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return (new string(chars.ToArray()), (int)Math.Round(length * Math.Log2(all.Length)));
    }

    /// <summary>Yazılmış bir şifrenin gücünü kabaca tahmin eder.</summary>
    public static int EstimateBits(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int pool = 0;
        if (text.Any(char.IsLower)) pool += 26;
        if (text.Any(char.IsUpper)) pool += 26;
        if (text.Any(char.IsDigit)) pool += 10;
        if (text.Any(c => !char.IsLetterOrDigit(c))) pool += 20;
        int effectiveLength = Math.Min(text.Length, text.Distinct().Count() * 2);
        return (int)Math.Round(effectiveLength * Math.Log2(Math.Max(pool, 2)));
    }

    public static string StrengthText(int bits)
    {
        string level = bits switch
        {
            < 40 => "Zayıf",
            < 60 => "Orta",
            < 80 => "Güçlü",
            _ => "Çok güçlü",
        };
        return $"{level} (yaklaşık {bits} bit)";
    }
}
