using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using KeyDone.Core;

namespace KeyDone;

public enum UnlockChoice { Cancelled, Opened, OpenOther, CreateNew }

public partial class UnlockWindow : Window
{
    readonly string? _path;
    readonly AppSettings _settings;
    readonly QuickUnlock _quick;
    readonly SecretInput _password;
    bool _pinMode;
    bool _busy;

    public UnlockChoice Choice { get; private set; } = UnlockChoice.Cancelled;
    public Vault? Vault { get; private set; }

    public UnlockWindow(string? path, AppSettings settings, QuickUnlock quick)
    {
        InitializeComponent();
        _settings = settings;
        _quick = quick;
        _password = new SecretInput("Ana şifre", PasswordLabel);
        PasswordHost.Content = _password.BuildPanel(new CheckBox { Content = "Şifreyi _göster" });

        bool exists = path is not null && File.Exists(path);
        _path = exists ? path : null;
        if (exists)
        {
            string help = $"Kasa dosyası: {path}";
            AutomationProperties.SetHelpText(_password.Hidden, help);
            AutomationProperties.SetHelpText(_password.Shown, help);
            AutomationProperties.SetHelpText(PinBox, help);
            if (quick.IsAvailable(settings, path)) SetPinMode(true);
            else InfoText.Text = help;
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
            if (_path is null)
            {
                OtherButton.Focus();
                Announcer.Say(InfoText.Text);
            }
            else if (_pinMode) PinBox.Focus();
            else _password.Focus();
        };
    }

    void SetPinMode(bool pin)
    {
        _pinMode = pin;
        PinPanel.Visibility = pin ? Visibility.Visible : Visibility.Collapsed;
        PasswordPanel.Visibility = pin ? Visibility.Collapsed : Visibility.Visible;
        ModeButton.Visibility = pin ? Visibility.Visible : Visibility.Collapsed;
        InfoText.Text = pin
            ? $"Kasa kilitli. PIN'inizi yazın. Kasa dosyası: {_path}"
            : $"Kasa dosyası: {_path}";
        PinBox.Clear();
        if (!IsLoaded) return;
        if (pin) PinBox.Focus(); else _password.Focus();
    }

    void Mode_Click(object sender, RoutedEventArgs e) => SetPinMode(false);

    // Dördüncü rakam yazılınca beklemeden denenir.
    void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_pinMode && PinBox.Password.Length == PinCode.Length) Open_Click(sender, e);
    }

    async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_path is null || _busy) return;
        string path = _path;
        _busy = true;
        IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            if (_pinMode) await OpenWithPin(path);
            else await OpenWithPassword(path);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            IsEnabled = true;
            _busy = false;
        }
        if (Choice == UnlockChoice.Opened) Close();
        else if (_pinMode) PinBox.Focus();
        else
        {
            _password.Focus();
            if (_password.Active is PasswordBox box) box.SelectAll();
            else if (_password.Active is TextBox text) text.SelectAll();
        }
    }

    async Task OpenWithPin(string path)
    {
        string pin = PinBox.Password;
        if (!PinCode.IsValidFormat(pin))
        {
            Ui.Error(this, "PIN 4 rakam olmalı.");
            return;
        }
        string? password = await Task.Run(() => _quick.TryPin(_settings, pin));
        if (password is null)
        {
            PinBox.Clear();
            if (_quick.IsAvailable(_settings, path))
            {
                Ui.Error(this, $"PIN yanlış. {_quick.AttemptsLeft} deneme hakkınız kaldı.");
            }
            else
            {
                Ui.Error(this, "PIN 3 kez yanlış girildi. Güvenliğiniz için PIN ile açma kapatıldı. Ana şifrenizi yazın.");
                SetPinMode(false);
            }
            return;
        }
        await Finish(path, password, rememberAgain: false);
    }

    async Task OpenWithPassword(string path)
    {
        string password = _password.Text;
        if (password.Length == 0)
        {
            Ui.Error(this, "Ana şifreyi yazın.");
            return;
        }
        await Finish(path, password, rememberAgain: true);
    }

    async Task Finish(string path, string password, bool rememberAgain)
    {
        Announcer.Say("Kasa açılıyor, lütfen bekleyin.");
        try
        {
            Vault = await Task.Run(() => Vault.Open(path, password));
        }
        catch (VaultException ex)
        {
            Ui.Error(this, ex.Message);
            // Kasa dosyası PIN'li açılıştan sonra başka bir şifreyle değiştirilmişse ana şifre istenir.
            if (_pinMode)
            {
                _quick.Forget();
                SetPinMode(false);
            }
            return;
        }
        if (rememberAgain) _quick.Remember(path, password);
        _password.Clear();
        Choice = UnlockChoice.Opened;
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
