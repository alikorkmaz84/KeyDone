using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyDone.Core;

namespace KeyDone;

public enum UnlockChoice { Cancelled, Opened, OpenOther, CreateNew }

public partial class UnlockWindow : Window
{
    readonly string? _path;
    readonly SecretInput _password;

    public UnlockChoice Choice { get; private set; } = UnlockChoice.Cancelled;
    public Vault? Vault { get; private set; }

    public UnlockWindow(string? path)
    {
        InitializeComponent();
        _password = new SecretInput("Ana şifre", PasswordLabel);
        PasswordHost.Content = _password.BuildPanel(new CheckBox { Content = "Şifreyi _göster" });

        bool exists = path is not null && File.Exists(path);
        _path = exists ? path : null;
        if (exists)
        {
            InfoText.Text = $"Kasa dosyası: {path}";
            AutomationPropertiesHelp($"Kasa dosyası: {path}");
        }
        else
        {
            InfoText.Text = path is null
                ? "Henüz bir kasa yok. Yeni bir kasa oluşturabilir ya da var olan bir KeePass kasasını açabilirsiniz."
                : $"Son kullanılan kasa bulunamadı: {path}. Başka bir kasa açabilir ya da yeni bir kasa oluşturabilirsiniz.";
            PasswordPanel.Visibility = Visibility.Collapsed;
            OpenButton.Visibility = Visibility.Collapsed;
            OpenButton.IsDefault = false;
            OtherButton.Content = "_Var olan bir kasayı aç...";
        }

        Loaded += (_, _) =>
        {
            if (_path is not null) _password.Focus();
            else OtherButton.Focus();
            if (_path is null) Announcer.Say(InfoText.Text);
        };
    }

    void AutomationPropertiesHelp(string text)
    {
        System.Windows.Automation.AutomationProperties.SetHelpText(_password.Hidden, text);
        System.Windows.Automation.AutomationProperties.SetHelpText(_password.Shown, text);
    }

    async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_path is null) return;
        string password = _password.Text;
        if (password.Length == 0)
        {
            Ui.Error(this, "Ana şifreyi yazın.");
            _password.Focus();
            return;
        }

        IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        Announcer.Say("Kasa açılıyor, lütfen bekleyin.");
        try
        {
            string path = _path;
            Vault = await Task.Run(() => Vault.Open(path, password));
            _password.Clear();
            Choice = UnlockChoice.Opened;
            Close();
        }
        catch (VaultException ex)
        {
            Mouse.OverrideCursor = null;
            IsEnabled = true;
            Ui.Error(this, ex.Message);
            _password.Focus();
            if (_password.Active is PasswordBox box) box.SelectAll();
            else if (_password.Active is TextBox text) text.SelectAll();
        }
        finally
        {
            Mouse.OverrideCursor = null;
            IsEnabled = true;
        }
    }

    void Other_Click(object sender, RoutedEventArgs e)
    {
        Choice = UnlockChoice.OpenOther;
        Close();
    }

    void New_Click(object sender, RoutedEventArgs e)
    {
        Choice = UnlockChoice.CreateNew;
        Close();
    }
}
