using System.Windows;
using System.Windows.Media.Imaging;
using ODBC.RecordingsTransfer.Services;

namespace ODBC.RecordingsTransfer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLogService.Write(args.Exception);
            System.Windows.MessageBox.Show(
                $"An unexpected error occurred:\n\n{args.Exception.Message}",
                "ODBC Recordings Transfer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}
