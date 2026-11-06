using KeyDone.Core;

namespace KeyDone.Core.Tests;

public sealed class VaultTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"keydone-test-{Guid.NewGuid():N}.kdbx");

    // Testler hızlı çalışsın diye düşük maliyetli anahtar türetme.
    static readonly KeePassLib.Cryptography.KeyDerivation.KdfParameters FastKdf = Vault.Argon2id(1, 1024 * 1024, 1);

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    static EntryField F(string key, string value, bool isProtected = false) => new(key, value, isProtected);

    [Fact]
    public void Kayit_kaydedilip_yeniden_acilinca_aynen_okunur()
    {
        string id;
        using (var vault = Vault.Create(_path, "doğru şifre", FastKdf))
        {
            id = vault.SaveEntry(new EntryData(null, "kart",
            [
                F("Title", "Maaş kartı"),
                F("Kart numarası", "4111 1111 1111 1111", true),
                F("Banka", "Örnek Bankası"),
                F("PIN", "", true),
            ]));
        }

        using var reopened = Vault.Open(_path, "doğru şifre");
        var entry = Assert.Single(reopened.List());
        Assert.Equal("Maaş kartı", entry.Title);
        Assert.Equal("kart", entry.Kind);

        var data = reopened.Get(id);
        Assert.True(data.Fields.Single(f => f.Key == "Kart numarası").IsProtected);
        Assert.DoesNotContain(data.Fields, f => f.Key == "PIN");
        Assert.Equal("4111 1111 1111 1111", reopened.Read(id, "Kart numarası"));
    }

    [Fact]
    public void Yanlis_sifre_anlasilir_hata_verir()
    {
        Vault.Create(_path, "doğru", FastKdf).Dispose();
        var ex = Assert.Throws<VaultException>(() => Vault.Open(_path, "yanlış"));
        Assert.Equal("Ana şifre yanlış.", ex.Message);
    }

    [Fact]
    public void Duzenleme_gecmise_eklenir_ve_sifre_her_zaman_gizlidir()
    {
        using var vault = Vault.Create(_path, "x", FastKdf);
        var id = vault.SaveEntry(new EntryData(null, "giris", [F("Title", "E-posta"), F("Password", "eski")]));
        vault.SaveEntry(new EntryData(id, "giris", [F("Title", "E-posta"), F("Password", "yeni")]));

        using var reopened = Vault.Open(_path, "x");
        Assert.Equal("yeni", reopened.Read(id, "Password"));
        Assert.True(reopened.Get(id).Fields.Single(f => f.Key == "Password").IsProtected);
    }

    [Fact]
    public void Silinen_kayit_listeden_kalkar()
    {
        using (var vault = Vault.Create(_path, "x", FastKdf))
        {
            var id = vault.SaveEntry(new EntryData(null, "not", [F("Title", "Not"), F("Gizli not", "gizli", true)]));
            vault.Delete(id);
        }
        using var reopened = Vault.Open(_path, "x");
        Assert.Empty(reopened.List());
    }

    [Fact]
    public void Bos_kayit_adi_reddedilir()
    {
        using var vault = Vault.Create(_path, "x", FastKdf);
        var ex = Assert.Throws<VaultException>(() => vault.SaveEntry(new EntryData(null, "not", [F("Title", "  ")])));
        Assert.Equal("Kayıt adı boş bırakılamaz.", ex.Message);
        Assert.Empty(vault.List());
    }
}

public sealed class SpellerTests
{
    [Fact]
    public void Harf_rakam_ve_isaretleri_ayirt_eder()
    {
        Assert.Equal(["küçük a", "büyük B", "rakam 1", "ünlem", "boşluk"], Speller.Spell("aB1! "));
    }

    [Fact]
    public void Turkce_i_harflerini_dogru_okur()
    {
        Assert.Equal(["küçük i, İzmir", "büyük I, Isparta", "küçük ı, Isparta", "büyük İ, İzmir"], Speller.Spell("iIıİ", phonetic: true));
    }
}

public sealed class GeneratorTests
{
    [Fact]
    public void Istenen_uzunlukta_ve_her_gruptan_karakterle_uretir()
    {
        for (int i = 0; i < 50; i++)
        {
            var (password, bits) = PasswordGenerator.Generate(new GeneratorOptions(Length: 12));
            Assert.Equal(12, password.Length);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, c => !char.IsLetterOrDigit(c));
            Assert.DoesNotContain(password, "Il1O0o|".Contains);
            Assert.True(bits > 60);
        }
    }

    [Fact]
    public void Hic_grup_secilmezse_bos_doner()
    {
        Assert.Equal("", PasswordGenerator.Generate(new GeneratorOptions(Upper: false, Lower: false, Digits: false, Symbols: false)).Password);
    }
}
