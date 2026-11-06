using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using KeyDone.Core;

namespace KeyDone;

public partial class GeneratorWindow : Window
{
    readonly AppSettings _settings;

    public string Password => OutputBox.Text;

    /// <param name="useMode">Kayıt penceresinden açıldıysa "Bu şifreyi kullan" düğmesi gösterilir.</param>
    public GeneratorWindow(AppSettings settings, bool useMode)
    {
        InitializeComponent();
        _settings = settings;
        if (!useMode)
        {
            UseButton.Visibility = Visibility.Collapsed;
            UseButton.IsDefault = false;
        }
        Generate(announce: false);
        Loaded += (_, _) => OutputBox.Focus();
        Closed += (_, _) => OutputBox.Clear();
    }

    void Generate(bool announce)
    {
        if (!int.TryParse(LengthBox.Text, out int length) || length < PasswordGenerator.MinLength || length > PasswordGenerator.MaxLength)
        {
            length = Math.Clamp(length, PasswordGenerator.MinLength, PasswordGenerator.MaxLength);
            LengthBox.Text = length.ToString();
        }
        var (password, bits) = PasswordGenerator.Generate(new GeneratorOptions(
            length,
            UpperBox.IsChecked == true,
            LowerBox.IsChecked == true,
            DigitsBox.IsChecked == true,
            SymbolsBox.IsChecked == true,
            AmbiguousBox.IsChecked == true));

        OutputBox.Text = password;
        StrengthText.Text = password.Length == 0 ? "En az bir karakter türü seçin." : $"Güç: {PasswordGenerator.StrengthText(bits)}";
        AutomationProperties.SetHelpText(OutputBox, StrengthText.Text);
        if (announce)
            Announcer.Say(password.Length == 0
                ? StrengthText.Text
                : $"Yeni şifre üretildi. {password.Length} karakter, {PasswordGenerator.StrengthText(bits)}.");
    }

    void Option_Changed(object sender, RoutedEventArgs e) => Generate(announce: true);

    void LengthBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Generate(announce: true);
        }
    }

    void Regenerate_Click(object sender, RoutedEventArgs e) => Generate(announce: true);

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Password.Length == 0) return;
        if (SecureClipboard.Copy(Password, _settings.ClipboardSeconds))
            Announcer.Say($"Şifre kopyalandı. {_settings.ClipboardSeconds} saniye sonra panodan silinecek.");
    }

    void Spell_Click(object sender, RoutedEventArgs e)
    {
        if (Password.Length == 0) return;
        var letters = Speller.Spell(Password, _settings.Phonetic);
        new ListWindow("Harf harf: Üretilen şifre", $"Üretilen şifre, {letters.Count} karakter:", letters) { Owner = this }.ShowDialog();
    }

    void Use_Click(object sender, RoutedEventArgs e)
    {
        if (Password.Length == 0)
        {
            Ui.Error(this, "En az bir karakter türü seçin.");
            return;
        }
        DialogResult = true;
    }
}
