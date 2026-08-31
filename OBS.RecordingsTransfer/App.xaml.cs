using System.Windows;
using System.Windows.Media.Imaging;
using OBS.RecordingsTransfer.Services;

namespace OBS.RecordingsTransfer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLogService.Write(args.Exception);
            System.Windows.MessageBox.Show(
                $"An unexpected error occurred:\n\n{args.Exception.Message}",
                "OBS Recordings Transfer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}
