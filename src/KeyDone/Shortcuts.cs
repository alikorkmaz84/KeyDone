using System.Runtime.InteropServices;
using System.Windows.Input;

namespace KeyDone;

/// <summary>Bir klavye kısayolu: değiştirici tuşlar ve bir tuş.</summary>
public readonly record struct Shortcut(ModifierKeys Modifiers, Key Key)
{
    public bool IsEmpty => Key == Key.None;

    public static Shortcut Of(Key key, ModifierKeys modifiers = ModifierKeys.None) => new(modifiers, key);

    public static Shortcut FromEvent(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        return new(Keyboard.Modifiers, key);
    }

    public bool IsModifierOnly => Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    bool Ctrl => Modifiers.HasFlag(ModifierKeys.Control);
    bool Alt => Modifiers.HasFlag(ModifierKeys.Alt);

    /// <summary>Kısayol atanabilir mi? Uygunsa null, değilse Türkçe nedeni döner.</summary>
    public string? Validate()
    {
        if (IsEmpty || IsModifierOnly) return "Bir tuş seçin.";
        bool functionKey = Key >= Key.F1 && Key <= Key.F24;
        if (Key == Key.F10) return "F10 menü çubuğu için ayrılmıştır.";
        if (Key == Key.F6 || (Key == Key.Tab && Ctrl)) return "Bu tuş kayıtlar ile alanlar arasında geçmek için ayrılmıştır.";
        if (Alt && Key == Key.F4) return "Alt F4 pencereyi kapatmak için ayrılmıştır.";
        if (Alt && !Ctrl && (Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9))
            return "Alt ile harf ve rakamlar menüler için ayrılmıştır. Control ile birlikte kullanın.";
        if (!Ctrl && !Alt && !functionKey && Key is not (Key.Delete or Key.Insert))
            return "Kısayol Control ya da Alt içermeli ya da bir F tuşu olmalı.";
        return null;
    }

    /// <summary>Metin kutusunda yazarken metin düzenlemeye ait olan tuşlar; orada kısayol olarak çalışmaz.</summary>
    public bool IsTextEditingKey =>
        (Modifiers == ModifierKeys.Control && Key is Key.C or Key.V or Key.X or Key.A or Key.Z or Key.Y or Key.Left or Key.Right or Key.Back or Key.Delete or Key.Home or Key.End)
        || (Modifiers == ModifierKeys.None && Key is Key.Delete or Key.Insert or Key.Back)
        || (Modifiers == ModifierKeys.Shift && Key is Key.Delete or Key.Insert);

    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    /// <summary>Ayar dosyasında saklama biçimi, örneğin "Control, Shift|N".</summary>
    public string Serialize() => IsEmpty ? "" : $"{Modifiers}|{Key}";

    public static Shortcut Deserialize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return default;
        var parts = text.Split('|');
        return parts.Length == 2
               && Enum.TryParse<ModifierKeys>(parts[0], out var mods)
               && Enum.TryParse<Key>(parts[1], out var key)
            ? new(mods, key)
            : default;
    }

    [DllImport("user32.dll")]
    static extern uint MapVirtualKeyW(uint code, uint mapType);

    static string KeyName(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9) return ((int)(key - Key.D0)).ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return $"Num {(int)(key - Key.NumPad0)}";
        switch (key)
        {
            case Key.Delete: return "Delete";
            case Key.Insert: return "Insert";
            case Key.Back: return "Backspace";
            case Key.PageUp: return "Page Up";
            case Key.PageDown: return "Page Down";
            case Key.Return: return "Enter";
            case Key.Space: return "Boşluk";
            case Key.Escape: return "Escape";
        }
        if (key.ToString().StartsWith("Oem", StringComparison.Ordinal))
        {
            // Klavye düzenine göre gerçek karakter (Türkçe Q klavyede "," "." "ö" gibi).
            uint ch = MapVirtualKeyW((uint)KeyInterop.VirtualKeyFromKey(key), 2) & 0x7FFF;
            if (ch != 0) return char.ToUpperInvariant((char)ch).ToString();
        }
        return key.ToString();
    }
}

/// <summary>Menüde görünen ve kısayol atanabilen bir işlem.</summary>
public sealed record CommandDef(string Id, string MenuText, string Title, Shortcut Default);

public static class Commands
{
    public const string NewVault = "vault.new";
    public const string OpenVault = "vault.open";
    public const string LockVault = "vault.lock";
    public const string NewEntry = "entry.new";
    public const string EditEntry = "entry.edit";
    public const string DeleteEntry = "entry.delete";
    public const string CopyPassword = "entry.copyPassword";
    public const string CopyUserName = "entry.copyUser";
    public const string Reveal = "field.reveal";
    public const string Spell = "entry.spell";
    public const string OpenUrl = "entry.openUrl";
    public const string Search = "view.search";
    public const string Generator = "tools.generator";
    public const string Shortcuts = "tools.shortcuts";
    public const string Settings = "tools.settings";
    public const string KeyboardHelp = "help.keys";
    public const string About = "help.about";

    const ModifierKeys C = ModifierKeys.Control;
    const ModifierKeys CS = ModifierKeys.Control | ModifierKeys.Shift;

    public static readonly IReadOnlyList<CommandDef> All =
    [
        new(NewVault, "_Yeni kasa oluştur...", "Yeni kasa oluştur", Shortcut.Of(Key.N, CS)),
        new(OpenVault, "Kasa _aç...", "Kasa aç", Shortcut.Of(Key.O, C)),
        new(LockVault, "_Kilitle", "Kasayı kilitle", Shortcut.Of(Key.L, C)),
        new(NewEntry, "_Yeni kayıt...", "Yeni kayıt", Shortcut.Of(Key.N, C)),
        new(EditEntry, "_Düzenle...", "Kaydı düzenle", Shortcut.Of(Key.E, C)),
        new(DeleteEntry, "_Sil...", "Kaydı sil", Shortcut.Of(Key.Delete)),
        new(CopyPassword, "Şifreyi _kopyala", "Şifreyi ya da seçili alanı kopyala", Shortcut.Of(Key.C, C)),
        new(CopyUserName, "Kullanıcı _adını kopyala", "Kullanıcı adını kopyala", Shortcut.Of(Key.B, C)),
        new(Reveal, "Alanı _göster ya da gizle", "Seçili alanı göster ya da gizle", Shortcut.Of(Key.R, C)),
        new(Spell, "_Harf harf oku", "Şifreyi ya da seçili alanı harf harf oku", Shortcut.Of(Key.H, C)),
        new(OpenUrl, "_Web sitesini aç", "Web sitesini aç", Shortcut.Of(Key.U, C)),
        new(Search, "_Ara", "Arama kutusuna git", Shortcut.Of(Key.F, C)),
        new(Generator, "Şifre _üretici...", "Şifre üretici", Shortcut.Of(Key.G, C)),
        new(Shortcuts, "_Kısayolları özelleştir...", "Kısayolları özelleştir", Shortcut.Of(Key.K, CS)),
        new(Settings, "_Ayarlar...", "Ayarlar", Shortcut.Of(Key.F9)),
        new(KeyboardHelp, "_Klavye kullanımı", "Klavye kullanımı", Shortcut.Of(Key.F1)),
        new(About, "KeyDone _hakkında", "KeyDone hakkında", default),
    ];

    public static CommandDef Get(string id) => All.First(c => c.Id == id);
}

/// <summary>Geçerli kısayollar: varsayılanlar ve kullanıcının değiştirdikleri.</summary>
public sealed class ShortcutMap
{
    readonly Dictionary<string, Shortcut> _current;

    public ShortcutMap(IReadOnlyDictionary<string, string>? overrides)
    {
        _current = Commands.All.ToDictionary(c => c.Id, c => c.Default);
        if (overrides is null) return;
        foreach (var (id, value) in overrides)
            if (_current.ContainsKey(id)) _current[id] = Shortcut.Deserialize(value);
    }

    public ShortcutMap Clone() => new(ToOverrides());

    public Shortcut Get(string id) => _current.GetValueOrDefault(id);

    public void Set(string id, Shortcut shortcut) => _current[id] = shortcut;

    public void Reset(string id) => _current[id] = Commands.Get(id).Default;

    public void ResetAll()
    {
        foreach (var c in Commands.All) _current[c.Id] = c.Default;
    }

    public string? Find(Shortcut shortcut) =>
        shortcut.IsEmpty ? null : _current.FirstOrDefault(kv => kv.Value == shortcut).Key;

    /// <summary>Yalnızca varsayılandan farklı olanlar saklanır.</summary>
    public Dictionary<string, string> ToOverrides() =>
        Commands.All.Where(c => _current[c.Id] != c.Default)
            .ToDictionary(c => c.Id, c => _current[c.Id].Serialize());
}
