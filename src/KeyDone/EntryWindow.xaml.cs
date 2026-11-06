using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using KeyDone.Core;

namespace KeyDone;

public partial class EntryWindow : Window
{
    sealed record Input(FieldDef Def, Func<string> Get, Action Focus, SecretInput? Secret);

    readonly AppSettings _settings;
    readonly EntryData? _existing;
    readonly HashSet<string> _protectedKeys;
    readonly List<Input> _inputs = [];
    RecordKind _kind;
    bool _dirty;
    bool _force;

    public EntryData? Result { get; private set; }

    public EntryWindow(AppSettings settings, EntryData? existing)
    {
        InitializeComponent();
        _settings = settings;
        _existing = existing;
        _kind = RecordKinds.Get(existing?.Kind);
        _protectedKeys = existing?.Fields.Where(f => f.IsProtected).Select(f => f.Key).ToHashSet() ?? [];

        Title = existing is null ? "Yeni kayıt – KeyDone" : $"Kaydı düzenle: {existing.Title} – KeyDone";
        KindBox.ItemsSource = RecordKinds.All;
        KindBox.SelectedItem = _kind;
        KindBox.SelectionChanged += KindBox_SelectionChanged;
        if (existing is not null) KindPanel.Visibility = Visibility.Collapsed;

        Build(existing?.Fields.ToDictionary(f => f.Key, f => f.Value) ?? []);
        _dirty = false;

        Loaded += (_, _) =>
        {
            if (existing is null) KindBox.Focus();
            else _inputs.FirstOrDefault()?.Focus();
        };
        Closing += OnClosing;
    }

    void Build(Dictionary<string, string> values)
    {
        FormPanel.Children.Clear();
        _inputs.Clear();

        var defs = _kind.Fields.ToList();
        // Başka KeePass uygulamalarıyla eklenmiş, şablonda olmayan alanlar da korunur ve düzenlenebilir.
        foreach (var (key, value) in values)
        {
            if (defs.Any(d => d.Key == key)) continue;
            defs.Add(new FieldDef(key, RecordKinds.LabelOf(_kind, key),
                Protected: _protectedKeys.Contains(key), Multiline: value.Contains('\n')));
        }

        foreach (var def in defs)
        {
            string value = values.GetValueOrDefault(def.Key, "");
            var label = new Label { Content = new TextBlock { Text = def.Label + (def.Required ? " (zorunlu):" : ":") } };
            FormPanel.Children.Add(label);
            UIElement inputElement;

            if (def.Protected && !def.Multiline)
            {
                var secret = new SecretInput(def.Label, label) { Text = value };
                var show = new CheckBox { Content = "Göster" };
                Ui.SetName(show, $"Göster: {def.Label}");
                var extras = new List<UIElement>();
                if (def.Generator)
                {
                    var generate = new Button { Content = "Üret...", Margin = new Thickness(8, 0, 0, 0), MinHeight = 0 };
                    Ui.SetName(generate, $"Şifre üret: {def.Label}");
                    generate.Click += (_, _) => Generate(secret);
                    extras.Add(generate);
                }
                secret.Changed += () => _dirty = true;
                inputElement = secret.BuildPanel(show, [.. extras]);
                SetHelp(secret.Hidden, def.Hint);
                SetHelp(secret.Shown, def.Hint);
                _inputs.Add(new Input(def, () => secret.Text, secret.Focus, secret));
            }
            else
            {
                var box = new TextBox { Text = value };
                if (def.Multiline)
                {
                    box.AcceptsReturn = true;
                    box.TextWrapping = TextWrapping.Wrap;
                    box.MinHeight = 90;
                    box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                }
                label.Target = box;
                Ui.SetName(box, def.Label);
                SetHelp(box, def.Hint);
                box.TextChanged += (_, _) => _dirty = true;
                inputElement = box;
                _inputs.Add(new Input(def, () => box.Text, () => box.Focus(), null));
            }

            FormPanel.Children.Add(inputElement);
            if (def.Hint is not null)
                FormPanel.Children.Add(new TextBlock { Text = def.Hint, Style = (Style)FindResource("Hint") });
        }
    }

    static void SetHelp(UIElement element, string? hint)
    {
        if (hint is not null) AutomationProperties.SetHelpText(element, hint);
    }

    Dictionary<string, string> Collect() => _inputs.ToDictionary(i => i.Def.Key, i => i.Get());

    void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KindBox.SelectedItem is not RecordKind kind || kind == _kind) return;
        // Yalnızca doldurulmuş alanlar yeni türe taşınır.
        var filled = Collect().Where(kv => kv.Value.Length > 0).ToDictionary();
        _kind = kind;
        Build(filled);
        Announcer.Say($"Tür: {kind.Label}. Alanlar güncellendi.");
    }

    void Generate(SecretInput target)
    {
        var dialog = new GeneratorWindow(_settings, useMode: true) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            target.Text = dialog.Password;
            target.Focus();
            Announcer.Say("Üretilen şifre forma yerleştirildi.");
        }
    }

    void Save_Click(object sender, RoutedEventArgs e) => Save();

    void Save()
    {
        var title = _inputs.First(i => i.Def.Key == StandardFields.Title);
        if (string.IsNullOrWhiteSpace(title.Get()))
        {
            Ui.Error(this, "Kayıt adı boş bırakılamaz.");
            title.Focus();
            return;
        }
        Result = new EntryData(_existing?.Id, _kind.Id,
            _inputs.Select(i => new EntryField(i.Def.Key, i.Get(), i.Def.Protected)).ToList());
        DialogResult = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            e.Handled = true;
            Save();
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.G)
        {
            e.Handled = true;
            var target = _inputs.FirstOrDefault(i => i.Secret is not null && i.Secret.Active.IsKeyboardFocusWithin)
                         ?? _inputs.FirstOrDefault(i => i.Def.Generator);
            if (target?.Secret is not null) Generate(target.Secret);
            else Announcer.Say("Bu kayıt türünde şifre alanı yok.");
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_force || DialogResult == true || !_dirty) return;
        if (!Ui.Confirm(this, "Değişiklikler kaydedilmedi. Kaydetmeden çıkmak istiyor musunuz?", "Kaydetmeden çık"))
            e.Cancel = true;
    }

    /// <summary>Kasa kilitlenirken soru sormadan kapatır.</summary>
    public void CloseWithoutPrompt()
    {
        _force = true;
        Close();
    }
}
