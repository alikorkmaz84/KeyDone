using System.Windows;
using System.Windows.Controls;

namespace KeyDone;

/// <summary>
/// Okunacak satırları bir listede gösterir (harf harf okuma, kısayollar).
/// Ekran okuyucu kullanıcısı oklarla satır satır gezer; liste konumu da okunur ("3 / 12" gibi).
/// </summary>
public partial class ListWindow : Window
{
    public ListWindow(string title, string caption, IEnumerable<string> lines)
    {
        InitializeComponent();
        Title = $"{title} – KeyDone";
        CaptionLabel.Content = new TextBlock { Text = caption };
        Ui.SetName(Items, caption.TrimEnd(':'));
        Items.ItemsSource = lines.ToList();
        Loaded += (_, _) => Ui.FocusSelected(Items);
    }
}
