using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KeyDone.Core;
using Microsoft.Win32;

namespace KeyDone;

public partial class CreateVaultWindow : Window
{
    readonly SecretInput _password;
    readonly SecretInput _repeat;
    readonly DispatcherTimer _strengthTimer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    string? _path;

    public Vault? Vault { get; private set; }

    readonly QuickUnlock _quick;

    public CreateVaultWindow(QuickUnlock quick)
    {
        InitializeComponent();
        _quick = quick;
        _password = new SecretInput("Ana şifre", PasswordLabel);
        _repeat = new SecretInput("Ana şifre (tekrar)", RepeatLabel);
        var show = new CheckBox { Content = "Şifreleri _göster" };
        show.Checked += (_, _) => _repeat.SetRevealed(true);
        show.Unchecked += (_, _) => _repeat.SetRevealed(false);
        PasswordHost.Content = _password.BuildPanel(show);
        RepeatHost.Content = _repeat.BuildPanel(new CheckBox { Visibility = Visibility.Collapsed });

        // Güç bilgisi yazarken kutunun açıklamasına eklenir; yazmaya ara verince sesli okunur.
        _password.Changed += () =>
        {
            int bits = PasswordGenerator.EstimateBits(_password.Text);
            StrengthText.Text = _password.Text.Length == 0 ? "" : $"Tahmini güç: {PasswordGenerator.StrengthText(bits)}";
            AutomationProperties.SetHelpText(_password.Hidden, StrengthText.Text);
            AutomationProperties.SetHelpText(_password.Shown, StrengthText.Text);
            _strengthTimer.Stop();
            if (StrengthText.Text.Length > 0) _strengthTimer.Start();
        };
        _strengthTimer.Tick += (_, _) =>
        {
            _strengthTimer.Stop();
            Announcer.Say(StrengthText.Text);
        };
        Loaded += (_, _) => FocusManager.SetFocusedElement(this, (IInputElement)FindName("PathBox"));
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Yeni kasanın konumu",
            FileName = "KeyDone.kdbx",
            DefaultExt = ".kdbx",
            Filter = "KeePass kasası (*.kdbx)|*.kdbx",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;
        _path = dialog.FileName;
        PathBox.Text = _path;
        Announcer.Say($"Konum seçildi: {_path}");
        _password.Focus();
    }

    async void Create_Click(object sender, RoutedEventArgs e)
    {
        string password = _password.Text;
        if (_path is null)
        {
            Ui.Error(this, "Kasa dosyası için bir konum seçin.");
            PathBox.Focus();
            return;
        }
        if (password.Length < 8)
        {
            Ui.Error(this, "Ana şifre en az 8 karakter olmalı.");
            _password.Focus();
            return;
        }
        if (password != _repeat.Text)
        {
            Ui.Error(this, "İki şifre birbirinin aynısı değil.");
            _repeat.Focus();
            return;
        }

        IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        Announcer.Say("Kasa oluşturuluyor, lütfen bekleyin.");
        try
        {
            string path = _path;
            Vault = await Task.Run(() => Vault.Create(path, password));
            _quick.Remember(path, password);
            _password.Clear();
            _repeat.Clear();
            DialogResult = true;
        }
        catch (VaultException ex)
        {
            Mouse.OverrideCursor = null;
            IsEnabled = true;
            Ui.Error(this, ex.Message);
            _password.Focus();
        }
        finally
        {
            Mouse.OverrideCursor = null;
            IsEnabled = true;
        }
    }
}
