using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KeyDone;

public partial class SettingsWindow : Window
{
    public const int GeneralTab = 0;
    public const int SoundsTab = 1;
    public const int ShortcutsTab = 2;

    public sealed record SoundRow(SoundEvent Event, string Title, string Source)
    {
        public string Spoken => $"{Title}, {Source}";
    }

    public sealed record ShortcutRow(string Id, string Title, string Keys)
    {
        public string Spoken => $"{Title}, {(Keys.Length == 0 ? "kısayol yok" : Keys)}";
    }

    readonly AppSettings _settings;

    /// <summary>Düzenlenen kısayollar; Tamam'a basılınca uygulanır.</summary>
    public ShortcutMap Shortcuts { get; }

    public SettingsWindow(AppSettings settings, ShortcutMap shortcuts, int tab)
    {
        InitializeComponent();
        _settings = settings;
        Shortcuts = shortcuts.Clone();

        foreach (int minutes in new[] { 1, 2, 5, 10, 15, 30, 0 })
            AutoLockBox.Items.Add(new ComboBoxItem { Content = minutes == 0 ? "Hiçbir zaman" : $"{minutes} dakika sonra", Tag = minutes });
        foreach (int seconds in new[] { 15, 30, 60, 90 })
            ClipboardBox.Items.Add(new ComboBoxItem { Content = $"{seconds} saniye sonra", Tag = seconds });
        foreach (var (label, value) in new[] { ("Düşük", 30), ("Orta", 60), ("Yüksek", 100) })
            VolumeBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });

        Select(AutoLockBox, settings.AutoLockMinutes, 5);
        Select(ClipboardBox, settings.ClipboardSeconds, 30);
        Select(VolumeBox, settings.SoundVolume, 60);
        PhoneticBox.IsChecked = settings.Phonetic;
        _pinSalt = settings.PinSalt;
        _pinHash = settings.PinHash;
        QuickUnlockBox.IsChecked = settings.QuickUnlockEnabled && _pinHash is not null;
        AutomationProperties_SetHelp(QuickUnlockBox, QuickUnlockHint.Text);
        UpdatePinButton();
        SoundsBox.IsChecked = settings.SoundsEnabled;
        SoundHint.Text = "Bir sesi değiştirmek için ses klasörüne şu adlardan biriyle WAV dosyası koyun: "
                         + string.Join(", ", Sounds.All.Select(s => s.FileName + ".wav")) + ".";
        AutomationProperties_SetHelp(SoundList, SoundHint.Text);

        RefreshSounds();
        RefreshShortcuts(null);
        Tabs.SelectedIndex = tab;
        Loaded += (_, _) =>
        {
            if (tab == ShortcutsTab) Ui.FocusSelected(ShortcutList);
            else ((TabItem)Tabs.SelectedItem).Focus();
        };
    }

    static void AutomationProperties_SetHelp(UIElement element, string text) =>
        System.Windows.Automation.AutomationProperties.SetHelpText(element, text);

    // ---------------- PIN ile hızlı açma ----------------

    string? _pinSalt;
    string? _pinHash;

    void UpdatePinButton() => PinButton.Content = _pinHash is null ? "PIN _belirle..." : "PIN'i _değiştir...";

    bool AskPin()
    {
        var dialog = new PinWindow { Owner = this };
        if (dialog.ShowDialog() != true) return false;
        (_pinSalt, _pinHash) = (dialog.Salt, dialog.Hash);
        UpdatePinButton();
        Announcer.Say("PIN belirlendi. Tamam'a basınca kaydedilir.");
        return true;
    }

    void QuickUnlockBox_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _pinHash is not null) return;
        // PIN'siz hızlı açma olmaz: önce PIN istenir, vazgeçilirse seçenek kapanır.
        if (!AskPin()) QuickUnlockBox.IsChecked = false;
        QuickUnlockBox.Focus();
    }

    void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (AskPin()) QuickUnlockBox.IsChecked = true;
        PinButton.Focus();
    }

    static void Select(ComboBox box, int value, int fallback)
    {
        var items = box.Items.Cast<ComboBoxItem>().ToList();
        box.SelectedItem = items.FirstOrDefault(i => (int)i.Tag == value) ?? items.First(i => (int)i.Tag == fallback);
    }

    static int Selected(ComboBox box) => (int)((ComboBoxItem)box.SelectedItem).Tag;

    // ---------------- Sesler ----------------

    void RefreshSounds()
    {
        SoundList.ItemsSource = Sounds.All
            .Select(s => new SoundRow(s.Event, s.Title, Sounds.CustomFile(s.Event) is null ? "Yerleşik ses" : "Kendi dosyanız"))
            .ToList();
        SoundList.SelectedIndex = 0;
    }

    void PlaySelected()
    {
        if (SoundList.SelectedItem is not SoundRow row) return;
        // Dinlerken seçili düzey kullanılır; ayarlar Tamam'a basınca kaydedilir.
        int saved = Sounds.Volume;
        Sounds.Volume = Selected(VolumeBox);
        Sounds.Play(row.Event, evenIfDisabled: true);
        Sounds.Volume = saved;
    }

    void SoundList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            PlaySelected();
        }
    }

    void Listen_Click(object sender, RoutedEventArgs e) => PlaySelected();

    void OpenSoundFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Sounds.Folder);
        Process.Start(new ProcessStartInfo(Sounds.Folder) { UseShellExecute = true });
    }

    // ---------------- Kısayollar ----------------

    void RefreshShortcuts(string? selectId)
    {
        selectId ??= (ShortcutList.SelectedItem as ShortcutRow)?.Id;
        var rows = Commands.All.Select(c => new ShortcutRow(c.Id, c.Title, Shortcuts.Get(c.Id).ToString())).ToList();
        ShortcutList.ItemsSource = rows;
        ShortcutList.SelectedItem = rows.FirstOrDefault(r => r.Id == selectId) ?? rows[0];
    }

    void FocusShortcuts(string id)
    {
        RefreshShortcuts(id);
        Ui.FocusSelected(ShortcutList);
    }

    void ChangeSelected()
    {
        if (ShortcutList.SelectedItem is not ShortcutRow row) return;
        var capture = new ShortcutCaptureWindow(row.Title, Shortcuts.Get(row.Id)) { Owner = this };
        if (capture.ShowDialog() != true) return;

        var chosen = capture.Shortcut;
        string? other = Shortcuts.Find(chosen);
        if (other is not null && other != row.Id)
        {
            string otherTitle = Commands.Get(other).Title;
            if (!Ui.Confirm(this, $"{chosen} şu anda “{otherTitle}” için kullanılıyor. Bu kısayol oradan kaldırılıp “{row.Title}” işlemine atansın mı?", "Kısayol çakışması"))
            {
                FocusShortcuts(row.Id);
                return;
            }
            Shortcuts.Set(other, default);
        }
        Shortcuts.Set(row.Id, chosen);
        FocusShortcuts(row.Id);
        Announcer.Say($"{row.Title}: {chosen}");
    }

    void ShortcutList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ChangeSelected();
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            RemoveSelected();
        }
    }

    void RemoveSelected()
    {
        if (ShortcutList.SelectedItem is not ShortcutRow row) return;
        Shortcuts.Set(row.Id, default);
        FocusShortcuts(row.Id);
        Announcer.Say($"{row.Title}: kısayol kaldırıldı.");
    }

    void Change_Click(object sender, RoutedEventArgs e) => ChangeSelected();

    void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelected();

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (ShortcutList.SelectedItem is not ShortcutRow row) return;
        var def = Commands.Get(row.Id).Default;
        string? other = Shortcuts.Find(def);
        if (other is not null && other != row.Id) Shortcuts.Set(other, default);
        Shortcuts.Reset(row.Id);
        FocusShortcuts(row.Id);
        Announcer.Say($"{row.Title}: varsayılana döndü, {(def.IsEmpty ? "kısayol yok" : def.ToString())}.");
    }

    void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.Confirm(this, "Tüm kısayollar varsayılana döndürülsün mü?", "Kısayollar")) return;
        Shortcuts.ResetAll();
        RefreshShortcuts(null);
        Ui.FocusSelected(ShortcutList);
        Announcer.Say("Tüm kısayollar varsayılana döndü.");
    }

    // ---------------- Kaydet ----------------

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoLockMinutes = Selected(AutoLockBox);
        _settings.ClipboardSeconds = Selected(ClipboardBox);
        _settings.Phonetic = PhoneticBox.IsChecked == true;
        _settings.QuickUnlockEnabled = QuickUnlockBox.IsChecked == true && _pinHash is not null;
        _settings.PinSalt = _pinSalt;
        _settings.PinHash = _pinHash;
        _settings.SoundsEnabled = SoundsBox.IsChecked == true;
        _settings.SoundVolume = Selected(VolumeBox);
        DialogResult = true;
    }
}
