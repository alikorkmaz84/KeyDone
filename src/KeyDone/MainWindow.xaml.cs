using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KeyDone.Core;
using Microsoft.Win32;

namespace KeyDone;

/// <summary>Kayıt listesindeki bir öğe. Ekran okuyucu ToString metnini okur.</summary>
public sealed class EntryItem(EntrySummary summary)
{
    public EntrySummary Summary { get; } = summary;

    public override string ToString()
    {
        var parts = new List<string>
        {
            string.IsNullOrWhiteSpace(Summary.Title) ? "Adsız kayıt" : Summary.Title,
            RecordKinds.Get(Summary.Kind).Label,
        };
        if (!string.IsNullOrWhiteSpace(Summary.UserName)) parts.Add(Summary.UserName);
        return string.Join(", ", parts);
    }
}

/// <summary>Alan listesindeki bir öğe. Gizli alanlar "gizli" diye okunur.</summary>
public sealed record FieldItem(string Key, string Label, string Value, bool IsProtected, bool Revealed = false)
{
    public override string ToString() => $"{Label}: {(IsProtected && !Revealed ? "gizli" : Value)}";
}

public partial class MainWindow : Window
{
    static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    readonly AppSettings _settings = AppSettings.Load();
    readonly ObservableCollection<EntryItem> _entries = [];
    readonly ObservableCollection<FieldItem> _fields = [];
    readonly DispatcherTimer _lockTimer = new();
    readonly DispatcherTimer _warnTimer = new();
    readonly DispatcherTimer _countTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    Vault? _vault;
    List<EntrySummary> _all = [];
    EntryData? _current;
    DateTime _lastActivity;
    bool _unlocking;

    public MainWindow()
    {
        InitializeComponent();
        EntryList.ItemsSource = _entries;
        FieldList.ItemsSource = _fields;

        KindFilter.Items.Add("Tüm türler");
        foreach (var kind in RecordKinds.All) KindFilter.Items.Add(kind);
        KindFilter.SelectedIndex = 0;

        _lockTimer.Tick += (_, _) => Lock("Uzun süre işlem yapılmadığı için kasa kilitlendi.");
        _warnTimer.Tick += (_, _) =>
        {
            _warnTimer.Stop();
            Announcer.Say("Kasa 30 saniye sonra kilitlenecek. Açık tutmak için herhangi bir tuşa basın.");
        };
        _countTimer.Tick += (_, _) =>
        {
            _countTimer.Stop();
            Announcer.Say(CountText());
        };

        InputManager.Current.PreProcessInput += OnAnyInput;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SecureClipboard.Cleared += () => Announcer.Say("Pano temizlendi.");

        Loaded += (_, _) => Dispatcher.BeginInvoke(() => ShowUnlockFlow(_settings.LastVaultPath), DispatcherPriority.ApplicationIdle);
        Closed += (_, _) =>
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SecureClipboard.ClearIfOurs();
            _vault?.Dispose();
        };
        UpdateState();
    }

    // ---------------- Kasa açma, oluşturma, kilitleme ----------------

    /// <summary>Kasa açma penceresini gösterir. Kullanıcı başka kasa ya da yeni kasa seçerse ona yönlendirir.</summary>
    void ShowUnlockFlow(string? path)
    {
        if (_unlocking) return;
        _unlocking = true;
        try
        {
            while (true)
            {
                var dialog = new UnlockWindow(path) { Owner = this };
                dialog.ShowDialog();
                switch (dialog.Choice)
                {
                    case UnlockChoice.Opened:
                        SetVault(dialog.Vault!, "Kasa açıldı.");
                        return;
                    case UnlockChoice.OpenOther:
                        path = PickVaultFile() ?? path;
                        continue;
                    case UnlockChoice.CreateNew:
                        if (CreateVault()) return;
                        continue;
                    default:
                        if (_vault is null) Announcer.Say("Kasa kilitli. Kasa açmak için Control O tuşlarına basın.");
                        return;
                }
            }
        }
        finally
        {
            _unlocking = false;
        }
    }

    string? PickVaultFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Kasa dosyası seç",
            Filter = "KeePass kasası (*.kdbx)|*.kdbx|Tüm dosyalar (*.*)|*.*",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    bool CreateVault()
    {
        var dialog = new CreateVaultWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Vault is null) return false;
        SetVault(dialog.Vault, "Kasa oluşturuldu.");
        return true;
    }

    void SetVault(Vault vault, string message)
    {
        CloseVault();
        _vault = vault;
        _settings.LastVaultPath = vault.FilePath;
        _settings.Save();
        SearchBox.Clear();
        KindFilter.SelectedIndex = 0;
        RefreshEntries(null);
        UpdateState();
        RestartAutoLock();
        // Kasa penceresi kapanıp ana pencere etkinleşince odak kayıt listesine verilir.
        Dispatcher.BeginInvoke(() => Ui.FocusSelected(EntryList), DispatcherPriority.ContextIdle);
        Announcer.Say(_all.Count == 0
            ? $"{message} Henüz kayıt yok. Yeni kayıt eklemek için Control N tuşlarına basın."
            : $"{message} {_all.Count} kayıt var.");
    }

    void CloseVault()
    {
        _lockTimer.Stop();
        _warnTimer.Stop();
        // Kilitlenirken açık pencereler soru sormadan kapanır; kaydedilmemiş değişiklikler kaybolur.
        foreach (var window in OwnedWindows.Cast<Window>().ToList())
        {
            if (window is EntryWindow entryWindow) entryWindow.CloseWithoutPrompt();
            else window.Close();
        }
        _vault?.Dispose();
        _vault = null;
        _all = [];
        _current = null;
        _entries.Clear();
        _fields.Clear();
        UpdateState();
    }

    void Lock(string message)
    {
        if (_vault is null) return;
        CloseVault();
        Announcer.Say(message);
        Dispatcher.BeginInvoke(() => ShowUnlockFlow(_settings.LastVaultPath), DispatcherPriority.ApplicationIdle);
    }

    void RestartAutoLock()
    {
        _lockTimer.Stop();
        _warnTimer.Stop();
        if (_vault is null || _settings.AutoLockMinutes <= 0) return;
        var total = TimeSpan.FromMinutes(_settings.AutoLockMinutes);
        _lockTimer.Interval = total;
        _lockTimer.Start();
        if (total > TimeSpan.FromSeconds(45))
        {
            _warnTimer.Interval = total - TimeSpan.FromSeconds(30);
            _warnTimer.Start();
        }
    }

    void OnAnyInput(object sender, PreProcessInputEventArgs e)
    {
        if (_vault is null || e.StagingItem.Input is not (KeyEventArgs or MouseButtonEventArgs)) return;
        var now = DateTime.UtcNow;
        if (now - _lastActivity < TimeSpan.FromSeconds(1)) return;
        _lastActivity = now;
        RestartAutoLock();
    }

    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
            Dispatcher.BeginInvoke(() => Lock("Windows kilitlendiği için kasa kilitlendi."));
    }

    void UpdateState()
    {
        bool open = _vault is not null;
        Title = open ? $"{Path.GetFileName(_vault!.FilePath)} – KeyDone" : "KeyDone – Kilitli";
        MenuEntry.IsEnabled = open;
        MenuLock.IsEnabled = open;
        StatusText.Text = open ? CountText() : "Kasa kilitli.";
    }

    bool Ready()
    {
        if (_vault is not null) return true;
        Announcer.Say("Kasa kilitli. Kasa açmak için Control O tuşlarına basın.");
        return false;
    }

    // ---------------- Liste ve arama ----------------

    EntrySummary? SelectedEntry => (EntryList.SelectedItem as EntryItem)?.Summary;

    void RefreshEntries(string? selectId)
    {
        if (_vault is null) return;
        var comparer = StringComparer.Create(Turkish, true);
        _all = _vault.List().OrderBy(e => e.Title, comparer).ToList();
        ApplyFilter(selectId);
    }

    static bool Matches(string text, string query) =>
        Turkish.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase) >= 0;

    void ApplyFilter(string? selectId = null)
    {
        selectId ??= SelectedEntry?.Id;
        string query = SearchBox.Text.Trim();
        var kind = KindFilter.SelectedItem as RecordKind;
        var visible = _all.Where(e =>
            (kind is null || RecordKinds.Get(e.Kind) == kind) &&
            (query.Length == 0 || Matches(e.Title, query) || Matches(e.UserName, query) || Matches(RecordKinds.Get(e.Kind).Label, query)));

        _entries.Clear();
        foreach (var e in visible) _entries.Add(new EntryItem(e));
        var keep = _entries.FirstOrDefault(i => i.Summary.Id == selectId);
        EntryList.SelectedItem = keep ?? _entries.FirstOrDefault();
        StatusText.Text = CountText();
        if (EntryList.SelectedItem is null) LoadFields();
    }

    string CountText()
    {
        if (_vault is null) return "Kasa kilitli.";
        int total = _all.Count, shown = _entries.Count;
        if (total == 0) return "Kasada henüz kayıt yok.";
        if (shown == total) return $"{total} kayıt.";
        if (shown == 0) return "Aramaya uyan kayıt yok.";
        return $"{shown} kayıt bulundu, toplam {total} kayıt.";
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_vault is null) return;
        ApplyFilter();
        _countTimer.Stop();
        _countTimer.Start();
    }

    void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Enter)
        {
            e.Handled = true;
            if (_entries.Count == 0) Announcer.Say(CountText());
            else Ui.FocusSelected(EntryList);
        }
    }

    void KindFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vault is null || !IsLoaded) return;
        ApplyFilter();
        Announcer.Say(CountText());
    }

    void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadFields();

    void EntryList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedEntry is not null)
        {
            e.Handled = true;
            if (_fields.Count == 0) Announcer.Say("Bu kayıtta başka bilgi yok.");
            else Ui.FocusSelected(FieldList);
        }
    }

    void EntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditEntry();

    // ---------------- Alanlar ----------------

    void LoadFields()
    {
        _fields.Clear();
        _current = null;
        var summary = SelectedEntry;
        if (summary is null || _vault is null)
        {
            FieldsLabel.Content = "Ala_nlar:";
            return;
        }
        _current = _vault.Get(summary.Id);
        var kind = RecordKinds.Get(_current.Kind);
        var order = kind.Fields.Select(f => f.Key).ToList();
        int Rank(string key) => order.IndexOf(key) is var i and >= 0 ? i : int.MaxValue;
        foreach (var f in _current.Fields
                     .Where(f => f.Key != StandardFields.Title && f.Value.Length > 0)
                     .OrderBy(f => Rank(f.Key)).ThenBy(f => f.Key, StringComparer.Create(Turkish, true)))
        {
            _fields.Add(new FieldItem(f.Key, RecordKinds.LabelOf(kind, f.Key), f.Value, f.IsProtected));
        }
        string title = string.IsNullOrWhiteSpace(summary.Title) ? "Adsız kayıt" : summary.Title;
        FieldsLabel.Content = $"Ala_nlar ({title.Replace("_", "__")}):";
        if (_fields.Count > 0) FieldList.SelectedIndex = 0;
    }

    void FieldList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var field = FieldList.SelectedItem as FieldItem;
        RevealButton.Content = field is { IsProtected: true, Revealed: true } ? "_Gizle" : "_Göster";
    }

    void FieldList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Ui.FocusSelected(EntryList);
        }
        else if (e.Key == Key.Enter && FieldList.SelectedItem is FieldItem field)
        {
            e.Handled = true;
            CopyValue(field.Label, field.Value);
        }
    }

    FieldItem? FocusedField => FieldList.IsKeyboardFocusWithin ? FieldList.SelectedItem as FieldItem : null;

    void CopyValue(string label, string value)
    {
        if (value.Length == 0)
        {
            Announcer.Say($"{label} bu kayıtta boş.");
            return;
        }
        if (SecureClipboard.Copy(value, _settings.ClipboardSeconds))
            Announcer.Say($"{label} kopyalandı. {_settings.ClipboardSeconds} saniye sonra panodan silinecek.");
        else
            Ui.Error(this, "Pano şu anda başka bir program tarafından kullanılıyor. Lütfen tekrar deneyin.");
    }

    void CopyKey(string key)
    {
        if (!Ready()) return;
        if (_current is null)
        {
            Announcer.Say("Önce bir kayıt seçin.");
            return;
        }
        var kind = RecordKinds.Get(_current.Kind);
        CopyValue(RecordKinds.LabelOf(kind, key), _current.Value(key));
    }

    void CopyPrimary()
    {
        if (FocusedField is { } field) CopyValue(field.Label, field.Value);
        else if (_current is not null) CopyKey(RecordKinds.Get(_current.Kind).PrimaryKey);
        else CopyKey(StandardFields.Password);
    }

    void CopyUserName()
    {
        if (_current is null)
        {
            CopyKey(StandardFields.UserName);
            return;
        }
        string key = _current.Value(StandardFields.UserName).Length > 0
            ? StandardFields.UserName
            : _current.Fields.FirstOrDefault(f => !f.IsProtected && f.Key != StandardFields.Title && f.Key != StandardFields.Notes && f.Value.Length > 0)?.Key
              ?? StandardFields.UserName;
        CopyKey(key);
    }

    void Spell(string label, string value)
    {
        if (value.Length == 0)
        {
            Announcer.Say($"{label} bu kayıtta boş.");
            return;
        }
        var letters = Speller.Spell(value, _settings.Phonetic);
        new ListWindow($"Harf harf: {label}", $"{label}, {letters.Count} karakter:", letters) { Owner = this }.ShowDialog();
    }

    void SpellPrimary()
    {
        if (!Ready()) return;
        if (FocusedField is { } field)
        {
            Spell(field.Label, field.Value);
            return;
        }
        if (_current is null)
        {
            Announcer.Say("Önce bir kayıt seçin.");
            return;
        }
        var kind = RecordKinds.Get(_current.Kind);
        Spell(RecordKinds.LabelOf(kind, kind.PrimaryKey), _current.Value(kind.PrimaryKey));
    }

    void OpenUrl()
    {
        if (!Ready()) return;
        string url = _current?.Value(StandardFields.Url).Trim() ?? "";
        if (url.Length == 0)
        {
            Announcer.Say("Bu kayıtta web adresi yok.");
            return;
        }
        if (!url.Contains("://")) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Announcer.Say("Yalnızca http ve https adresleri açılabilir.");
            return;
        }
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        Announcer.Say("Site tarayıcıda açılıyor.");
    }

    // ---------------- Kayıt ekleme, düzenleme, silme ----------------

    void NewEntry()
    {
        if (!Ready()) return;
        var dialog = new EntryWindow(_settings, null) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null) SaveEntry(dialog.Result);
    }

    void EditEntry()
    {
        if (!Ready()) return;
        if (_current is null)
        {
            Announcer.Say("Önce bir kayıt seçin.");
            return;
        }
        var dialog = new EntryWindow(_settings, _current) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null) SaveEntry(dialog.Result);
    }

    void SaveEntry(EntryData data)
    {
        if (_vault is null) return; // pencere açıkken kasa kilitlenmiş olabilir
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            string id = _vault.SaveEntry(data);
            SearchBox.Clear();
            KindFilter.SelectedIndex = 0;
            RefreshEntries(id);
            Ui.FocusSelected(EntryList);
            Announcer.Say("Kayıt kaydedildi.");
        }
        catch (VaultException ex)
        {
            Ui.Error(this, ex.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    void DeleteEntry()
    {
        if (!Ready()) return;
        var summary = SelectedEntry;
        if (summary is null)
        {
            Announcer.Say("Önce bir kayıt seçin.");
            return;
        }
        string title = string.IsNullOrWhiteSpace(summary.Title) ? "Adsız kayıt" : summary.Title;
        if (!Ui.Confirm(this, $"“{title}” kaydı kalıcı olarak silinsin mi? Bu işlem geri alınamaz.", "Kaydı sil"))
            return;
        if (_vault is null) return;
        try
        {
            int index = EntryList.SelectedIndex;
            Mouse.OverrideCursor = Cursors.Wait;
            _vault.Delete(summary.Id);
            RefreshEntries(null);
            if (_entries.Count > 0) EntryList.SelectedIndex = Math.Min(index, _entries.Count - 1);
            Ui.FocusSelected(EntryList);
            Announcer.Say($"“{title}” silindi. {CountText()}");
        }
        catch (VaultException ex)
        {
            Ui.Error(this, ex.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // ---------------- Klavye kısayolları ----------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool ctrl = mods == ModifierKeys.Control;
        bool ctrlShift = mods == (ModifierKeys.Control | ModifierKeys.Shift);
        bool inText = Keyboard.FocusedElement is TextBox or PasswordBox;

        Action? action = null;
        if (mods == ModifierKeys.None && key == Key.F1) action = ShowShortcuts;
        else if (ctrlShift && key == Key.N) action = () => CreateVault();
        else if (ctrl)
        {
            action = key switch
            {
                Key.F => () => { SearchBox.Focus(); SearchBox.SelectAll(); },
                Key.N => NewEntry,
                Key.O => OpenVaultFromMenu,
                Key.L => () => Lock("Kasa kilitlendi."),
                Key.G => ShowGenerator,
                Key.E => EditEntry,
                Key.B => CopyUserName,
                Key.C when !inText => CopyPrimary,
                Key.U => OpenUrl,
                Key.H => SpellPrimary,
                _ => null,
            };
        }
        else if (mods == ModifierKeys.None && key == Key.Delete && !inText) action = DeleteEntry;

        if (action is null)
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        e.Handled = true;
        action();
    }

    // ---------------- Menüler ve düğmeler ----------------

    void OpenVaultFromMenu()
    {
        var path = PickVaultFile();
        if (path is not null) ShowUnlockFlow(path);
    }

    void ShowGenerator() => new GeneratorWindow(_settings, useMode: false) { Owner = this }.ShowDialog();

    void ShowShortcuts()
    {
        string[] lines =
        [
            "Control F: Arama kutusuna git",
            "Aşağı ok veya Enter (arama kutusunda): Kayıt listesine geç",
            "Enter (kayıt listesinde): Kaydın alanlarına geç",
            "Enter (alan listesinde): Seçili alanı kopyala",
            "Escape (alan listesinde): Kayıt listesine dön",
            "Control N: Yeni kayıt",
            "Control E: Seçili kaydı düzenle",
            "Delete: Seçili kaydı sil (onay istenir)",
            "Control C: Şifreyi ya da seçili alanı kopyala",
            "Control B: Kullanıcı adını kopyala",
            "Control H: Şifreyi ya da seçili alanı harf harf oku",
            "Control U: Web sitesini aç",
            "Control G: Şifre üretici",
            "Control L: Kasayı kilitle",
            "Control O: Başka bir kasa aç",
            "Control Shift N: Yeni kasa oluştur",
            "Control S (kayıt penceresinde): Kaydet",
            "F1: Bu listeyi aç",
        ];
        new ListWindow("Klavye kısayolları", "Kısayollar:", lines) { Owner = this }.ShowDialog();
    }

    void NewVault_Click(object sender, RoutedEventArgs e) => CreateVault();
    void OpenVault_Click(object sender, RoutedEventArgs e) => OpenVaultFromMenu();
    void Lock_Click(object sender, RoutedEventArgs e) => Lock("Kasa kilitlendi.");
    void Exit_Click(object sender, RoutedEventArgs e) => Close();
    void NewEntry_Click(object sender, RoutedEventArgs e) => NewEntry();
    void EditEntry_Click(object sender, RoutedEventArgs e) => EditEntry();
    void DeleteEntry_Click(object sender, RoutedEventArgs e) => DeleteEntry();
    void CopyPrimary_Click(object sender, RoutedEventArgs e) => CopyPrimary();
    void CopyUserName_Click(object sender, RoutedEventArgs e) => CopyUserName();
    void SpellPrimary_Click(object sender, RoutedEventArgs e) => SpellPrimary();
    void OpenUrl_Click(object sender, RoutedEventArgs e) => OpenUrl();
    void Generator_Click(object sender, RoutedEventArgs e) => ShowGenerator();
    void Shortcuts_Click(object sender, RoutedEventArgs e) => ShowShortcuts();

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (new SettingsWindow(_settings) { Owner = this }.ShowDialog() == true)
        {
            _settings.Save();
            RestartAutoLock();
            Announcer.Say("Ayarlar kaydedildi.");
        }
    }

    void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            "KeyDone 0.2\n\nEkran okuyucu kullanıcıları için tasarlanmış şifre ve önemli bilgi kasası.\n" +
            "Kasa dosyası KeePass (KDBX 4) biçimindedir; KeePassXC, KeePassDX ve Strongbox ile de açılabilir.",
            "KeyDone hakkında", MessageBoxButton.OK, MessageBoxImage.Information);

    void CopyField_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready()) return;
        if (FieldList.SelectedItem is FieldItem field) CopyValue(field.Label, field.Value);
        else Announcer.Say("Önce bir alan seçin.");
    }

    void SpellField_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready()) return;
        if (FieldList.SelectedItem is FieldItem field) Spell(field.Label, field.Value);
        else Announcer.Say("Önce bir alan seçin.");
    }

    void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready()) return;
        if (FieldList.SelectedItem is not FieldItem field)
        {
            Announcer.Say("Önce bir alan seçin.");
            return;
        }
        if (!field.IsProtected)
        {
            Announcer.Say(field.ToString());
            return;
        }
        int index = FieldList.SelectedIndex;
        var updated = field with { Revealed = !field.Revealed };
        _fields[index] = updated;
        FieldList.SelectedIndex = index;
        Announcer.Say(updated.Revealed ? $"{field.Label}: {field.Value}" : $"{field.Label} gizlendi.");
    }
}
