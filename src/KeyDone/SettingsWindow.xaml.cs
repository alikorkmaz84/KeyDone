using System.Windows;
using System.Windows.Controls;

namespace KeyDone;

public partial class SettingsWindow : Window
{
    readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        foreach (int minutes in new[] { 1, 2, 5, 10, 15, 30, 0 })
            AutoLockBox.Items.Add(new ComboBoxItem { Content = minutes == 0 ? "Hiçbir zaman" : $"{minutes} dakika sonra", Tag = minutes });
        foreach (int seconds in new[] { 15, 30, 60, 90 })
            ClipboardBox.Items.Add(new ComboBoxItem { Content = $"{seconds} saniye sonra", Tag = seconds });

        Select(AutoLockBox, settings.AutoLockMinutes, 5);
        Select(ClipboardBox, settings.ClipboardSeconds, 30);
        PhoneticBox.IsChecked = settings.Phonetic;
        Loaded += (_, _) => AutoLockBox.Focus();
    }

    static void Select(ComboBox box, int value, int fallback)
    {
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == value)
                           ?? box.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == fallback);
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoLockMinutes = (int)((ComboBoxItem)AutoLockBox.SelectedItem).Tag;
        _settings.ClipboardSeconds = (int)((ComboBoxItem)ClipboardBox.SelectedItem).Tag;
        _settings.Phonetic = PhoneticBox.IsChecked == true;
        DialogResult = true;
    }
}
