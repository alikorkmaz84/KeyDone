using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace KeyDone;

/// <summary>
/// Kullanıcı yeni kısayolun tuşlarına basar, kutu tuşları gösterir ve okur.
/// Tab, Enter ve Escape tek başına basılınca her zamanki gibi çalışır (gezinme, Tamam, Vazgeç).
/// </summary>
public partial class ShortcutCaptureWindow : Window
{
    public Shortcut Shortcut { get; private set; }

    public ShortcutCaptureWindow(string commandTitle, Shortcut current)
    {
        InitializeComponent();
        Title = $"Kısayol ata: {commandTitle} – KeyDone";
        Shortcut = current;
        IntroText.Text = $"“{commandTitle}” için yeni kısayolun tuşlarına basın, sonra Enter'a basın. " +
                         "Örnek: Control ile birlikte bir harf ya da bir F tuşu.";
        KeyBox.Text = current.IsEmpty ? "Kısayol yok" : current.ToString();
        AutomationProperties.SetHelpText(KeyBox, IntroText.Text);
        Loaded += (_, _) => KeyBox.Focus();
    }

    void KeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var pressed = Shortcut.FromEvent(e);
        bool plain = pressed.Modifiers is ModifierKeys.None or ModifierKeys.Shift;
        if (plain && pressed.Key is Key.Tab or Key.Enter or Key.Escape) return; // gezinme, Tamam ve Vazgeç
        e.Handled = true;
        if (pressed.IsModifierOnly) return;

        string? problem = pressed.Validate();
        if (problem is not null)
        {
            ProblemText.Text = $"{pressed}: {problem}";
            Ui.Error(this, ProblemText.Text);
            KeyBox.Focus();
            return;
        }
        ProblemText.Text = "";
        Shortcut = pressed;
        KeyBox.Text = pressed.ToString();
        Announcer.Say(pressed.ToString());
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Shortcut.IsEmpty)
        {
            Ui.Error(this, "Önce yeni kısayolun tuşlarına basın.");
            KeyBox.Focus();
            return;
        }
        DialogResult = true;
    }
}
