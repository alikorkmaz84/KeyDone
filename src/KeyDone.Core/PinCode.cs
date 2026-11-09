using System.Security.Cryptography;
using System.Text;

namespace KeyDone.Core;

/// <summary>
/// Hızlı kilit açma PIN'i. PIN'in kendisi saklanmaz; tuzlu ve yavaşlatılmış (PBKDF2) bir özeti saklanır.
/// PIN tek başına kasayı açmaz: yalnızca program açıkken bellekte tutulan ana şifrenin kilidini açar.
/// </summary>
public static class PinCode
{
    public const int Length = 4;
    public const int MaxAttempts = 3;
    const int Iterations = 600_000;

    public static bool IsValidFormat(string pin) => pin.Length == Length && pin.All(char.IsAsciiDigit);

    /// <summary>Kolay tahmin edilen PIN'ler: hepsi aynı rakam ya da ardışık rakamlar.</summary>
    public static bool IsTooSimple(string pin)
    {
        if (!IsValidFormat(pin)) return true;
        if (pin.Distinct().Count() == 1) return true;
        bool ascending = true, descending = true;
        for (int i = 1; i < pin.Length; i++)
        {
            ascending &= pin[i] - pin[i - 1] == 1;
            descending &= pin[i - 1] - pin[i] == 1;
        }
        return ascending || descending;
    }

    public static (string Salt, string Hash) Create(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(Derive(pin, salt)));
    }

    public static bool Verify(string pin, string? salt, string? hash)
    {
        if (salt is null || hash is null || !IsValidFormat(pin)) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Derive(pin, Convert.FromBase64String(salt)), Convert.FromBase64String(hash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    static byte[] Derive(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, 32);
}
