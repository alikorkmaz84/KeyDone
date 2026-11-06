using System.Windows;

namespace KeyDone;

public partial class App : Application
{
    public App()
    {
        // Beklenmeyen bir hata uygulamayı sessizce kapatmasın; kullanıcıya okunabilir bir mesaj gösterilsin.
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu:\n\n{e.Exception.Message}",
                "KeyDone – Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
