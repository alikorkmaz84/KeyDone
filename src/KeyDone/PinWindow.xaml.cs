using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using KeyDone.Core;

namespace KeyDone;

public partial class PinWindow : Window
{
    public string? Salt { get; private set; }
    public string? Hash { get; private set; }

    public PinWindow()
    {
        InitializeComponent();
        AutomationProperties.SetHelpText(PinBox, IntroText.Text);
        Loaded += (_, _) => PinBox.Focus();
    }

    async void Ok_Click(object sender, RoutedEventArgs e)
    {
        string pin = PinBox.Password;
        if (!PinCode.IsValidFormat(pin))
        {
            Ui.Error(this, "PIN tam olarak 4 rakam olmalı.");
            PinBox.Focus();
            return;
        }
        if (PinCode.IsTooSimple(pin))
        {
            Ui.Error(this, "Bu PIN tahmin edilmesi kolay (1234, 0000 gibi). Başka bir PIN seçin.");
            PinBox.Clear();
            RepeatBox.Clear();
            PinBox.Focus();
            return;
        }
        if (pin != RepeatBox.Password)
        {
            Ui.Error(this, "İki PIN birbirinin aynısı değil.");
            RepeatBox.Clear();
            RepeatBox.Focus();
            return;
        }
        IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            (Salt, Hash) = await Task.Run(() => PinCode.Create(pin));
        }
        finally
        {
            Mouse.OverrideCursor = null;
            IsEnabled = true;
        }
        PinBox.Clear();
        RepeatBox.Clear();
        DialogResult = true;
    }
}
