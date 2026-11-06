namespace KeyDone.Core;

/// <summary>Bir kayıt türündeki tek alanın tanımı.</summary>
public sealed record FieldDef(
    string Key,
    string Label,
    bool Protected = false,
    bool Multiline = false,
    bool Required = false,
    bool Generator = false,
    bool Primary = false,
    string? Hint = null);

/// <summary>Kayıt türü: giriş bilgisi, banka kartı gibi.</summary>
public sealed record RecordKind(string Id, string Label, IReadOnlyList<FieldDef> Fields)
{
    /// <summary>Ctrl+C ile kopyalanan ana gizli alan.</summary>
    public string PrimaryKey => Fields.FirstOrDefault(f => f.Primary)?.Key ?? StandardFields.Password;

    public override string ToString() => Label;
}

public static class StandardFields
{
    public const string Title = "Title";
    public const string UserName = "UserName";
    public const string Password = "Password";
    public const string Url = "URL";
    public const string Notes = "Notes";

    public static bool IsStandard(string key) => key is Title or UserName or Password or Url or Notes;
}

/// <summary>
/// Kayıt türleri. Title, UserName, Password, URL ve Notes KeePass'in standart alanlarıdır;
/// diğerleri kasada Türkçe adlarıyla özel alan olarak saklanır, böylece KeePassXC, KeePassDX
/// ve Strongbox gibi uygulamalarda da okunabilir.
/// </summary>
public static class RecordKinds
{
    static readonly FieldDef TitleField = new(StandardFields.Title, "Kayıt adı", Required: true);
    static readonly FieldDef NotesField = new(StandardFields.Notes, "Notlar", Multiline: true);

    public static readonly IReadOnlyList<RecordKind> All =
    [
        new("giris", "Giriş bilgisi",
        [
            TitleField,
            new(StandardFields.UserName, "Kullanıcı adı veya e-posta"),
            new(StandardFields.Password, "Şifre", Protected: true, Generator: true, Primary: true),
            new(StandardFields.Url, "Web adresi", Hint: "Örnek: https://www.ornek.com"),
            NotesField,
        ]),
        new("kart", "Banka kartı",
        [
            TitleField,
            new("Kart sahibi", "Kart sahibi"),
            new("Kart numarası", "Kart numarası", Protected: true, Primary: true),
            new("Son kullanma tarihi", "Son kullanma tarihi", Hint: "Ay ve yıl, örnek: 05/2029"),
            new("Güvenlik kodu", "Güvenlik kodu (CVV)", Protected: true),
            new("PIN", "PIN", Protected: true),
            new("Banka", "Banka"),
            NotesField,
        ]),
        new("hesap", "Banka hesabı",
        [
            TitleField,
            new("Banka", "Banka"),
            new("Hesap sahibi", "Hesap sahibi"),
            new("IBAN", "IBAN", Hint: "TR ile başlayan 26 karakter"),
            new("Hesap numarası", "Hesap numarası"),
            new("Şube", "Şube"),
            new(StandardFields.UserName, "Müşteri numarası veya internet bankacılığı kullanıcı adı"),
            new(StandardFields.Password, "İnternet bankacılığı şifresi", Protected: true, Primary: true),
            NotesField,
        ]),
        new("kimlik", "Kimlik belgesi",
        [
            TitleField,
            new("Belge türü", "Belge türü", Hint: "Örnek: kimlik kartı, pasaport, ehliyet"),
            new("Ad soyad", "Ad soyad"),
            new("T.C. kimlik numarası", "T.C. kimlik numarası", Protected: true, Primary: true),
            new("Belge numarası", "Belge numarası", Protected: true),
            new("Veriliş tarihi", "Veriliş tarihi", Hint: "Örnek: 14.03.2022"),
            new("Geçerlilik tarihi", "Geçerlilik tarihi", Hint: "Örnek: 14.03.2032"),
            NotesField,
        ]),
        new("wifi", "Wi-Fi ağı",
        [
            TitleField,
            new("Ağ adı", "Ağ adı (SSID)"),
            new(StandardFields.Password, "Wi-Fi şifresi", Protected: true, Generator: true, Primary: true),
            new("Güvenlik türü", "Güvenlik türü", Hint: "Örnek: WPA2, WPA3"),
            NotesField,
        ]),
        new("lisans", "Yazılım lisansı",
        [
            TitleField,
            new("Yazılım", "Yazılım"),
            new("Lisans anahtarı", "Lisans anahtarı", Protected: true, Primary: true),
            new(StandardFields.UserName, "Kayıtlı e-posta"),
            new("Satın alma tarihi", "Satın alma tarihi"),
            NotesField,
        ]),
        new("kripto", "Kripto cüzdanı",
        [
            TitleField,
            new("Kurtarma kelimeleri", "Kurtarma kelimeleri", Protected: true, Multiline: true, Primary: true),
            new("Cüzdan adresi", "Cüzdan adresi"),
            new(StandardFields.Password, "Cüzdan şifresi", Protected: true, Generator: true),
            NotesField,
        ]),
        new("not", "Güvenli not",
        [
            TitleField,
            new("Gizli not", "Not", Protected: true, Multiline: true, Primary: true),
        ]),
    ];

    public static RecordKind Default => All[0];

    /// <summary>Bilinmeyen ya da boş türler (başka KeePass uygulamalarından gelen kayıtlar) giriş bilgisi sayılır.</summary>
    public static RecordKind Get(string? id) => All.FirstOrDefault(k => k.Id == id) ?? Default;

    public static string LabelOf(RecordKind kind, string key) =>
        kind.Fields.FirstOrDefault(f => f.Key == key)?.Label ?? key switch
        {
            StandardFields.Title => "Kayıt adı",
            StandardFields.UserName => "Kullanıcı adı",
            StandardFields.Password => "Şifre",
            StandardFields.Url => "Web adresi",
            StandardFields.Notes => "Notlar",
            _ => key,
        };
}
