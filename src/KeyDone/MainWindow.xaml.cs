using System.Collections.ObjectModel;
using System.ComponentModel;
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

/// <summary>Kayıt listesindeki bir satır.</summary>
public sealed class EntryItem(EntrySummary summary)
{
    public EntrySummary Summary { get; } = summary;
    public string Title => string.IsNullOrWhiteSpace(Summary.Title) ? "Adsız kayıt" : Summary.Title;
    public string KindLabel => RecordKinds.Get(Summary.Kind).Label;
    public string UserName => Summary.UserName;

    /// <summary>Ekran okuyucunun okuduğu metin.</summary>
    public string Spoken => string.IsNullOrWhiteSpace(UserName) ? $"{Title}, {KindLabel}" : $"{Title}, {KindLabel}, {UserName}";
}

/// <summary>Alan listesindeki bir satır. Gizli alanlar "gizli" diye gösterilir ve okunur.</summary>
public sealed class FieldItem(string key, string label, string value, bool isProtected) : INotifyPropertyChanged
{
    bool _revealed;

    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Value { get; } = value;
    public bool IsProtected { get; } = isProtected;

    public bool Revealed
    {
        get => _revealed;
        set
        {
            _revealed = value;
            PropertyChanged?.Invoke(this, new(nameof(Display)));
            PropertyChanged?.Invoke(this, new(nameof(Spoken)));
        }
    }

    public string Display => IsProtected && !Revealed ? "gizli" : Value.ReplaceLineEndings(" / ");
    public string Spoken => $"{Label}: {Display}";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainWindow : Window
{
    static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    readonly AppSettings _settings = AppSettings.Load();
    readonly ShortcutMap _shortcuts;
    readonly QuickUnlock _quick = new();
    readonly Dictionary<string, Action> _actions;
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
        _shortcuts = new ShortcutMap(_settings.Shortcuts);
        Sounds.Enabled = _settings.SoundsEnabled;
        Sounds.Volume = _settings.SoundVolume;

        _actions = new()
        {
            [Commands.NewVault] = () => CreateVault(),
            [Commands.OpenVault] = OpenVaultFromMenu,
            [Commands.LockVault] = () => Lock("Kasa kilitlendi."),
            [Commands.NewEntry] = NewEntry,
            [Commands.EditEntry] = EditEntry,
            [Commands.DeleteEntry] = DeleteEntry,
            [Commands.CopyPassword] = CopyPrimary,
            [Commands.CopyUserName] = CopyUserName,
            [Commands.Reveal] = RevealField,
            [Commands.Spell] = SpellPrimary,
            [Commands.OpenUrl] = OpenUrl,
            [Commands.Search] = () => { SearchBox.Focus(); SearchBox.SelectAll(); },
            [Commands.Generator] = () => new GeneratorWindow(_settings, useMode: false) { Owner = this }.ShowDialog(),
            [Commands.Shortcuts] = () => ShowSettings(SettingsWindow.ShortcutsTab),
            [Commands.Settings] = () => ShowSettings(SettingsWindow.GeneralTab),
            [Commands.KeyboardHelp] = ShowKeyboardHelp,
            [Commands.About] = ShowAbout,
        };

        EntryList.ItemsSource = _entries;
        FieldList.ItemsSource = _fields;
        // Kategori sekmeleri: Tümü ve her kayıt türü. Sekmelerin içeriği yoktur; yalnızca listeyi süzer.
        CategoryTabs.Items.Add(new TabItem { Header = "Tümü", Tag = null });
        foreach (var kind in RecordKinds.All) CategoryTabs.Items.Add(new TabItem { Header = kind.Label, Tag = kind });
        CategoryTabs.SelectedIndex = 0;
        BuildMenus();

        _lockTimer.Tick += (_, _) => Lock("Uzun süre işlem yapılmadığı için kasa kilitlendi.");
        _warnTimer.Tick += (_, _) =>
        {
            _warnTimer.Stop();
            Sounds.Play(SoundEvent.Warning);
            Announcer.Say("Kasa 30 saniye sonra kilitlenecek. Açık tutmak için herhangi bir tuşa basın.");
        };
        _countTimer.Tick += (_, _) =>
        {
            _countTimer.Stop();
            Announcer.Say(CountText());
        };

        InputManager.Current.PreProcessInput += OnAnyInput;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SecureClipboard.Cleared += () =>
        {
            Sounds.Play(SoundEvent.Cleared);
            Announcer.Say("Pano temizlendi.");
        };

        Loaded += (_, _) => Dispatcher.BeginInvoke(() => ShowUnlockFlow(_settings.LastVaultPath), DispatcherPriority.ApplicationIdle);
        Closed += (_, _) =>
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SecureClipboard.ClearIfOurs();
            _quick.Forget();
            _vault?.Dispose();
        };
        UpdateState();
    }

    // ---------------- Menüler ----------------

    const string Separator = "-";
    const string Exit = "exit";
    const string SwitchPaneItem = "switch";
    const string NextCategoryItem = "nextCategory";
    const string PreviousCategoryItem = "previousCategory";

    void BuildMenus()
    {
        MainMenu.Items.Clear();
        MainMenu.Items.Add(TopMenu("_Dosya", Commands.NewVault, Commands.OpenVault, Separator, Commands.LockVault, Separator, Exit));
        MainMenu.Items.Add(TopMenu("_Kayıt", Commands.NewEntry, Commands.EditEntry, Commands.DeleteEntry, Separator,
            Commands.CopyPassword, Commands.CopyUserName, Commands.Reveal, Commands.Spell, Commands.OpenUrl));
        MainMenu.Items.Add(TopMenu("_Görünüm", Commands.Search, Separator, NextCategoryItem, PreviousCategoryItem, Separator, SwitchPaneItem));
        MainMenu.Items.Add(TopMenu("_Araçlar", Commands.Generator, Separator, Commands.Shortcuts, Commands.Settings));
        MainMenu.Items.Add(TopMenu("_Yardım", Commands.KeyboardHelp, Commands.About));

        // Uygulama tuşu ya da Shift+F10 ile açılan menüler.
        EntryList.ContextMenu = Context(Commands.EditEntry, Commands.DeleteEntry, Separator,
            Commands.CopyPassword, Commands.CopyUserName, Commands.Spell, Commands.OpenUrl, Separator, Commands.NewEntry);
        FieldList.ContextMenu = Context(Commands.CopyPassword, Commands.Reveal, Commands.Spell, Separator, Commands.EditEntry);
        // Bağlam menüsü açılınca odak menüye geçer; işlemin alan listesi için olduğu buradan anlaşılır.
        FieldList.ContextMenu.Opened += (_, _) => _fieldMenuOpen = true;
        FieldList.ContextMenu.Closed += (_, _) =>
            Dispatcher.BeginInvoke(() => _fieldMenuOpen = false, DispatcherPriority.Background);
    }

    bool _fieldMenuOpen;

    MenuItem TopMenu(string header, params string[] ids)
    {
        var top = new MenuItem { Header = header };
        foreach (var id in ids) top.Items.Add(MenuEntry(id));
        return top;
    }

    ContextMenu Context(params string[] ids)
    {
        var menu = new ContextMenu();
        foreach (var id in ids) menu.Items.Add(MenuEntry(id));
        return menu;
    }

    object MenuEntry(string id)
    {
        switch (id)
        {
            case Separator:
                return new Separator();
            case Exit:
                return MenuCommand("Çı_kış", "Alt+F4", Close);
            case SwitchPaneItem:
                return MenuCommand("Kayıtlar ile alanlar arasında _geç", "F6", SwitchPane);
            case NextCategoryItem:
                return MenuCommand("_Sonraki kategori", "Ctrl+Tab", () => MoveCategory(+1));
            case PreviousCategoryItem:
                return MenuCommand("Ö_nceki kategori", "Ctrl+Shift+Tab", () => MoveCategory(-1));
            default:
                return MenuCommand(Commands.Get(id).MenuText, _shortcuts.Get(id).ToString(), () => Run(id));
        }
    }

    static MenuItem MenuCommand(string header, string gesture, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture };
        // Ekran okuyucu kısayolu da söylesin ("Yeni kayıt, Ctrl+N").
        System.Windows.Automation.AutomationProperties.SetAcceleratorKey(item, gesture);
        item.Click += (_, _) => action();
        return item;
    }

    void Run(string id)
    {
        if (_actions.TryGetValue(id, out var action)) action();
    }

    // ---------------- Klavye ----------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var pressed = Shortcut.FromEvent(e);

        // Ctrl+Tab ve Ctrl+Shift+Tab: sonraki ya da önceki kategori.
        if (pressed.Key == Key.Tab && pressed.Modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            MoveCategory(pressed.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : +1);
            return;
        }
        // F6 ve Shift+F6: kayıt listesi ile alan listesi arasında geç.
        if (pressed.Key == Key.F6 && pressed.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            e.Handled = true;
            SwitchPane();
            return;
        }

        string? id = _shortcuts.Find(pressed);
        bool inText = Keyboard.FocusedElement is TextBox or PasswordBox;
        if (id is null || (inText && pressed.IsTextEditingKey))
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        e.Handled = true;
        Run(id);
    }

    void SwitchPane()
    {
        if (FieldList.IsKeyboardFocusWithin) Ui.FocusSelected(EntryList);
        else if (_fields.Count > 0) Ui.FocusSelected(FieldList);
        else if (_vault is null) Announcer.Say("Kasa kilitli.");
        else
        {
            Ui.FocusSelected(EntryList);
            Announcer.Say(_current is null ? "Önce bir kayıt seçin." : "Bu kayıtta başka bilgi yok.");
        }
    }

    // ---------------- Kasa açma, oluşturma, kilitleme ----------------

    void ShowUnlockFlow(string? path)
    {
        if (_unlocking) return;
        _unlocking = true;
        try
        {
            while (true)
            {
                var dialog = new UnlockWindow(path, _settings, _quick) { Owner = this };
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
                        if (_vault is null) Announcer.Say($"Kasa kilitli. Kasa açmak için {_shortcuts.Get(Commands.OpenVault)}.");
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

    void OpenVaultFromMenu()
    {
        var path = PickVaultFile();
        if (path is not null) ShowUnlockFlow(path);
    }

    bool CreateVault()
    {
        var dialog = new CreateVaultWindow(_quick) { Owner = this };
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
        CategoryTabs.SelectedIndex = 0;
        RefreshEntries(null);
        UpdateState();
        RestartAutoLock();
        Sounds.Play(SoundEvent.Opened);
        // Kasa penceresi kapanıp ana pencere etkinleşince odak kayıt listesine verilir.
        Dispatcher.BeginInvoke(() => Ui.FocusSelected(EntryList), DispatcherPriority.ContextIdle);
        Announcer.Say(_all.Count == 0
            ? $"{message} Henüz kayıt yok. Yeni kayıt için {_shortcuts.Get(Commands.NewEntry)}."
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
        UpdateCategoryHeaders();
        UpdateState();
    }

    void Lock(string message)
    {
        if (_vault is null) return;
        CloseVault();
        // PIN ile açma kapalıysa bellekte tutulan ana şifre de silinir.
        if (!_settings.QuickUnlockEnabled || _settings.PinHash is null) _quick.Forget();
        Sounds.Play(SoundEvent.Locked);
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
        Title = _vault is null ? "KeyDone – Kilitli" : $"{Path.GetFileName(_vault.FilePath)} – KeyDone";
        StatusText.Text = CountText();
    }

    bool Ready()
    {
        if (_vault is not null) return true;
        Announcer.Say($"Kasa kilitli. Kasa açmak için {_shortcuts.Get(Commands.OpenVault)}.");
        return false;
    }

    bool ReadyWithEntry()
    {
        if (!Ready()) return false;
        if (_current is not null) return true;
        Announcer.Say("Önce bir kayıt seçin.");
        return false;
    }

    // ---------------- Kayıt listesi ve arama ----------------

    EntrySummary? SelectedEntry => (EntryList.SelectedItem as EntryItem)?.Summary;

    void RefreshEntries(string? selectId)
    {
        if (_vault is null) return;
        _all = _vault.List().OrderBy(e => e.Title, StringComparer.Create(Turkish, true)).ToList();
        UpdateCategoryHeaders();
        ApplyFilter(selectId);
    }

    RecordKind? SelectedKind => (CategoryTabs.SelectedItem as TabItem)?.Tag as RecordKind;

    /// <summary>Sekme adlarında kayıt sayısı da okunur: "Banka kartı (2)".</summary>
    void UpdateCategoryHeaders()
    {
        foreach (TabItem tab in CategoryTabs.Items)
        {
            var kind = tab.Tag as RecordKind;
            string label = kind?.Label ?? "Tümü";
            tab.Header = _vault is null ? label : $"{label} ({_all.Count(e => kind is null || RecordKinds.Get(e.Kind) == kind)})";
        }
    }

    void MoveCategory(int step)
    {
        if (!Ready()) return;
        int count = CategoryTabs.Items.Count;
        bool listHadFocus = EntryList.IsKeyboardFocusWithin;
        CategoryTabs.SelectedIndex = ((CategoryTabs.SelectedIndex + step) % count + count) % count;
        // Liste yenilenince odak kaybolmasın. Kategorilerdeyken odak yeni sekmeye geçer ve
        // ekran okuyucu sekmenin adını (kayıt sayısıyla) zaten okur; ayrıca duyuru yapılmaz.
        if (CategoryTabs.IsKeyboardFocusWithin)
        {
            ((TabItem)CategoryTabs.SelectedItem).Focus();
            return;
        }
        if (listHadFocus && _entries.Count > 0) Ui.FocusSelected(EntryList);
        Announcer.Say($"{((TabItem)CategoryTabs.SelectedItem).Header}. {CountText()}", queue: listHadFocus);
    }

    /// <summary>Kategorilerde dört ok tuşu da çalışır; Home ve End ilk ve son kategoriye gider.</summary>
    void CategoryTabs_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        int count = CategoryTabs.Items.Count;
        switch (e.Key)
        {
            case Key.Up or Key.Left:
                MoveCategory(-1);
                break;
            case Key.Down or Key.Right:
                MoveCategory(+1);
                break;
            case Key.Home:
                MoveCategory(-CategoryTabs.SelectedIndex);
                break;
            case Key.End:
                MoveCategory(count - 1 - CategoryTabs.SelectedIndex);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void CategoryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != CategoryTabs || _vault is null || !IsLoaded) return;
        ApplyFilter();
    }

    static bool Matches(string text, string query) =>
        Turkish.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase) >= 0;

    void ApplyFilter(string? selectId = null)
    {
        selectId ??= SelectedEntry?.Id;
        string query = SearchBox.Text.Trim();
        var kind = SelectedKind;
        var visible = _all.Where(e =>
            (kind is null || RecordKinds.Get(e.Kind) == kind) &&
            (query.Length == 0 || Matches(e.Title, query) || Matches(e.UserName, query) || Matches(RecordKinds.Get(e.Kind).Label, query)));

        _entries.Clear();
        foreach (var e in visible) _entries.Add(new EntryItem(e));
        EntryList.SelectedItem = _entries.FirstOrDefault(i => i.Summary.Id == selectId) ?? _entries.FirstOrDefault();
        StatusText.Text = CountText();
        if (EntryList.SelectedItem is null) LoadFields();
    }

    string CountText()
    {
        if (_vault is null) return "Kasa kilitli.";
        int total = _all.Count, shown = _entries.Count;
        if (total == 0) return "Kasada henüz kayıt yok.";
        if (shown == total) return $"{total} kayıt.";
        if (shown == 0) return SearchBox.Text.Trim().Length > 0 ? "Aramaya uyan kayıt yok." : "Bu kategoride kayıt yok.";
        return $"{shown} kayıt, toplam {total} kayıt.";
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
        if (e.Key is Key.Down or Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (_entries.Count == 0) Announcer.Say(CountText());
            else Ui.FocusSelected(EntryList);
        }
    }

    void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadFields();

    void EntryList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && SelectedEntry is not null)
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

    void FieldList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
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

    /// <summary>Alan listesi ya da onun bağlam menüsü kullanılıyorsa seçili alan.</summary>
    FieldItem? ActiveField =>
        FieldList.IsKeyboardFocusWithin || _fieldMenuOpen ? FieldList.SelectedItem as FieldItem : null;

    void CopyValue(string label, string value)
    {
        if (value.Length == 0)
        {
            Announcer.Say($"{label} bu kayıtta boş.");
            return;
        }
        if (SecureClipboard.Copy(value, _settings.ClipboardSeconds))
        {
            Sounds.Play(SoundEvent.Copied);
            Announcer.Say($"{label} kopyalandı. {_settings.ClipboardSeconds} saniye sonra panodan silinecek.");
        }
        else
        {
            Ui.Error(this, "Pano şu anda başka bir program tarafından kullanılıyor. Lütfen tekrar deneyin.");
        }
    }

    void CopyKey(string key)
    {
        var kind = RecordKinds.Get(_current!.Kind);
        CopyValue(RecordKinds.LabelOf(kind, key), _current.Value(key));
    }

    void CopyPrimary()
    {
        if (!ReadyWithEntry()) return;
        if (ActiveField is { } field) CopyValue(field.Label, field.Value);
        else CopyKey(RecordKinds.Get(_current!.Kind).PrimaryKey);
    }

    void CopyUserName()
    {
        if (!ReadyWithEntry()) return;
        string key = _current!.Value(StandardFields.UserName).Length > 0
            ? StandardFields.UserName
            : _current.Fields.FirstOrDefault(f => !f.IsProtected && f.Key != StandardFields.Title && f.Key != StandardFields.Notes && f.Value.Length > 0)?.Key
              ?? StandardFields.UserName;
        CopyKey(key);
    }

    void RevealField()
    {
        if (!ReadyWithEntry()) return;
        var field = FieldList.SelectedItem as FieldItem;
        if (field is null)
        {
            Announcer.Say("Bu kayıtta gösterilecek alan yok.");
            return;
        }
        if (!field.IsProtected)
        {
            Announcer.Say(field.Spoken);
            return;
        }
        field.Revealed = !field.Revealed;
        Announcer.Say(field.Revealed ? $"{field.Label}: {field.Value}" : $"{field.Label} gizlendi.");
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
        if (!ReadyWithEntry()) return;
        if (ActiveField is { } field)
        {
            Spell(field.Label, field.Value);
            return;
        }
        var kind = RecordKinds.Get(_current!.Kind);
        Spell(RecordKinds.LabelOf(kind, kind.PrimaryKey), _current.Value(kind.PrimaryKey));
    }

    void OpenUrl()
    {
        if (!ReadyWithEntry()) return;
        string url = _current!.Value(StandardFields.Url).Trim();
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
        if (!ReadyWithEntry()) return;
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
            // Kaydedilen kayıt açık kategoride görünmüyorsa "Tümü"ne geçilir.
            if (SelectedKind is { } kind && kind.Id != data.Kind) CategoryTabs.SelectedIndex = 0;
            RefreshEntries(id);
            Ui.FocusSelected(EntryList);
            Sounds.Play(SoundEvent.Saved);
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
        if (!ReadyWithEntry()) return;
        var summary = SelectedEntry!;
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
            Sounds.Play(SoundEvent.Deleted);
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

    // ---------------- Ayarlar ve yardım ----------------

    void ShowSettings(int tab)
    {
        var dialog = new SettingsWindow(_settings, _shortcuts, tab) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        foreach (var c in Commands.All) _shortcuts.Set(c.Id, dialog.Shortcuts.Get(c.Id));
        _settings.Shortcuts = _shortcuts.ToOverrides();
        _settings.Save();
        Sounds.Enabled = _settings.SoundsEnabled;
        Sounds.Volume = _settings.SoundVolume;
        BuildMenus();
        RestartAutoLock();
        Announcer.Say("Ayarlar kaydedildi.");
    }

    void ShowKeyboardHelp()
    {
        var lines = new List<string>
        {
            "Tab: Arama kutusu, tür, kayıt listesi ve alan listesi arasında gezer",
            "Ctrl+Tab ve Ctrl+Shift+Tab: Sonraki ve önceki kategori",
            "Sağ ve sol ok (kategorilerde): Kategori değiştirir",
            "F6: Kayıt listesi ile alan listesi arasında geçer",
            "Aşağı ve yukarı ok: Listelerin içinde gezer",
            "Alt ya da F10: Menü çubuğunu açar (Tab ile menüye gidilmez)",
            "Uygulama tuşu ya da Shift+F10: Seçili kayıt veya alan için işlemler menüsünü açar",
            "Aşağı ok ya da Enter (arama kutusunda): Kayıt listesine geçer",
            "Enter (kayıt listesinde): Kaydın alanlarına geçer",
            "Enter (alan listesinde): Seçili alanı kopyalar",
            "Escape (alan listesinde): Kayıt listesine döner",
        };
        foreach (var c in Commands.All)
        {
            var shortcut = _shortcuts.Get(c.Id);
            lines.Add($"{(shortcut.IsEmpty ? "Kısayol yok" : shortcut.ToString())}: {c.Title}");
        }
        lines.Add("Kısayolları Araçlar menüsündeki Kısayolları özelleştir ile değiştirebilirsiniz.");
        new ListWindow("Klavye kullanımı", "Klavye kullanımı:", lines) { Owner = this }.ShowDialog();
    }

    void ShowAbout() =>
        MessageBox.Show(this,
            "KeyDone 0.3\n\nEkran okuyucu kullanıcıları için tasarlanmış şifre ve önemli bilgi kasası.\n" +
            "Kasa dosyası KeePass (KDBX 4) biçimindedir; KeePassXC, KeePassDX ve Strongbox ile de açılabilir.",
            "KeyDone hakkında", MessageBoxButton.OK, MessageBoxImage.None);
}
