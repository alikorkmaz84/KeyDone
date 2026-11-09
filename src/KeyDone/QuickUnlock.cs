using System.Security.Cryptography;
using System.Text;
using KeyDone.Core;

namespace KeyDone;

/// <summary>
/// Kasa kilitlendikten sonra, program kapanana kadar 4 haneli PIN ile yeniden açma.
///
/// Ana şifre bellekte Windows'un kullanıcıya bağlı şifrelemesiyle (DPAPI) korunarak tutulur;
/// program kapanınca bu bilgi yok olur ve yalnızca ana şifre işe yarar.
/// PIN 3 kez yanlış girilirse saklanan bilgi silinir ve ana şifre istenir.
/// </summary>
public sealed class QuickUnlock
{
    // Bu çalışmaya özel ek anahtar: korunan bilgi başka bir süreçte çözülemez.
    static readonly byte[] Entropy = RandomNumberGenerator.GetBytes(32);

    byte[]? _protectedPassword;
    string? _path;
    int _failures;

    public int AttemptsLeft => PinCode.MaxAttempts - _failures;

    public void Remember(string path, string password)
    {
        Forget();
        byte[] plain = Encoding.UTF8.GetBytes(password);
        try
        {
            _protectedPassword = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            _path = path;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Forget()
    {
        if (_protectedPassword is not null) CryptographicOperations.ZeroMemory(_protectedPassword);
        _protectedPassword = null;
        _path = null;
        _failures = 0;
    }

    public bool IsAvailable(AppSettings settings, string? path) =>
        settings.QuickUnlockEnabled
        && settings.PinHash is not null
        && _protectedPassword is not null
        && path is not null
        && string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)
        && _failures < PinCode.MaxAttempts;

    /// <summary>PIN doğruysa ana şifreyi döndürür; yanlışsa null. Hak biterse saklanan bilgi silinir.</summary>
    public string? TryPin(AppSettings settings, string pin)
    {
        if (_protectedPassword is null) return null;
        if (!PinCode.Verify(pin, settings.PinSalt, settings.PinHash))
        {
            _failures++;
            if (_failures >= PinCode.MaxAttempts)
            {
                var path = _path;
                Forget();
                _failures = PinCode.MaxAttempts;
                _path = path;
            }
            return null;
        }
        _failures = 0;
        byte[] plain = ProtectedData.Unprotect(_protectedPassword, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
