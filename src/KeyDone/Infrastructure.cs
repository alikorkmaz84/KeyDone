using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KeyDone;

/// <summary>Bu bilgisayara özel ayarlar (%APPDATA%\KeyDone\settings.json). Gizli bilgi içermez.</summary>
public sealed class AppSettings
{
    public string? LastVaultPath { get; set; }
    public int AutoLockMinutes { get; set; } = 5;
    public int ClipboardSeconds { get; set; } = 30;
    public bool Phonetic { get; set; }

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyDone", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception)
        {
            // Bozuk ayar dosyası varsayılanlarla değiştirilir.
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Ayarlar kaydedilemezse uygulama yine çalışır.
        }
    }
}

/// <summary>
/// Ekran okuyucuya duyuru gönderir (UI Automation bildirim olayı).
/// NVDA, JAWS ve Windows Ekran Okuyucusu bu bildirimleri okur.
/// </summary>
public static class Announcer
{
    public static void Say(string message)
    {
        var app = Application.Current;
        if (app is null) return;
        var target = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
        if (target is null) return;
        var peer = UIElementAutomationPeer.FromElement(target) ?? UIElementAutomationPeer.CreatePeerForElement(target);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            message,
            "KeyDone.Duyuru");
    }
}

/// <summary>
/// Panoya kopyalar ve belirli süre sonra panoyu temizler.
/// Kopyalanan bilgi Windows pano geçmişine ve buluta gönderilmez.
/// </summary>
public static class SecureClipboard
{
    static readonly DispatcherTimer Timer = new();
    static string? _copied;

    public static event Action? Cleared;

    static SecureClipboard()
    {
        Timer.Tick += (_, _) =>
        {
            Timer.Stop();
            if (ClearIfOurs()) Cleared?.Invoke();
        };
    }

    public static bool Copy(string text, int seconds)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        // Windows'a bu içeriği pano geçmişine, buluta ve pano izleme programlarına vermemesini söyler.
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                _copied = text;
                Timer.Stop();
                Timer.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 600));
                Timer.Start();
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(50); // pano başka bir program tarafından kısa süre meşgul olabilir
            }
        }
        return false;
    }

    /// <summary>Panoda hâlâ KeyDone'un kopyaladığı bilgi varsa siler. Kullanıcının sonradan kopyaladığına dokunmaz.</summary>
    public static bool ClearIfOurs()
    {
        if (_copied is null) return false;
        try
        {
            bool ours = Clipboard.ContainsText() && Clipboard.GetText() == _copied;
            if (ours) Clipboard.Clear();
            return ours;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _copied = null;
        }
    }
}

public static class Ui
{
    /// <summary>Listede seçili öğeye (yoksa ilk öğeye) odaklanır; ekran okuyucu öğeyi okur.</summary>
    public static void FocusSelected(ListBox list)
    {
        if (list.Items.Count == 0)
        {
            list.Focus();
            return;
        }
        if (list.SelectedIndex < 0) list.SelectedIndex = 0;
        list.UpdateLayout();
        list.ScrollIntoView(list.SelectedItem);
        if (list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) is ListBoxItem item)
            item.Focus();
        else
            list.Focus();
    }

    public static void Error(Window owner, string message) =>
        MessageBox.Show(owner, message, "KeyDone", MessageBoxButton.OK, MessageBoxImage.Warning);

    public static bool Confirm(Window owner, string message, string title) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            == MessageBoxResult.Yes;

    public static void SetName(UIElement element, string name) => AutomationProperties.SetName(element, name);
}

/// <summary>
/// Gizli bir metin kutusu: normalde PasswordBox, "Göster" seçilince düz metin kutusu.
/// Etiket her zaman görünen kutuyu gösterir, böylece Alt kısayolu ve ekran okuyucu doğru kutuya gider.
/// </summary>
public sealed class SecretInput
{
    public PasswordBox Hidden { get; } = new();
    public TextBox Shown { get; } = new() { Visibility = Visibility.Collapsed };
    public bool Revealed { get; private set; }
    readonly Label? _label;

    public event Action? Changed;

    public SecretInput(string name, Label? label = null)
    {
        _label = label;
        Ui.SetName(Hidden, name);
        Ui.SetName(Shown, name);
        Hidden.PasswordChanged += (_, _) => Changed?.Invoke();
        Shown.TextChanged += (_, _) => Changed?.Invoke();
        if (_label is not null) _label.Target = Hidden;
    }

    public string Text
    {
        get => Revealed ? Shown.Text : Hidden.Password;
        set
        {
            Hidden.Password = value;
            Shown.Text = value;
        }
    }

    public Control Active => Revealed ? Shown : Hidden;

    public void SetRevealed(bool reveal)
    {
        if (reveal == Revealed) return;
        bool hadFocus = Active.IsKeyboardFocusWithin;
        if (reveal) Shown.Text = Hidden.Password; else Hidden.Password = Shown.Text;
        Revealed = reveal;
        Shown.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
        Hidden.Visibility = reveal ? Visibility.Collapsed : Visibility.Visible;
        if (_label is not null) _label.Target = Active;
        if (hadFocus) Active.Focus();
    }

    public void Focus() => Active.Focus();

    public void Clear()
    {
        Hidden.Clear();
        Shown.Clear();
    }

    /// <summary>Kutu ve "Göster" onay kutusunu yan yana içeren panel.</summary>
    public FrameworkElement BuildPanel(CheckBox showBox, params UIElement[] extras)
    {
        showBox.Margin = new Thickness(8, 0, 0, 0);
        showBox.VerticalAlignment = VerticalAlignment.Center;
        showBox.Checked += (_, _) => SetRevealed(true);
        showBox.Unchecked += (_, _) => SetRevealed(false);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var boxes = new Grid();
        boxes.Children.Add(Hidden);
        boxes.Children.Add(Shown);
        grid.Children.Add(boxes);
        var side = new StackPanel { Orientation = Orientation.Horizontal };
        side.Children.Add(showBox);
        foreach (var extra in extras) side.Children.Add(extra);
        Grid.SetColumn(side, 1);
        grid.Children.Add(side);
        return grid;
    }
}
