using System.Windows;
using ODBC.RecordingsTransfer.Models;
using ODBC.RecordingsTransfer.Services;

namespace ODBC.RecordingsTransfer.Views;

public partial class UpdateWindow : Window
{
    private readonly UpdateService _updateService;
    private readonly UpdateInfo _update;
    private CancellationTokenSource? _cts;

    public UpdateWindow(UpdateService updateService, UpdateInfo update)
    {
        InitializeComponent();
        _updateService = updateService;
        _update = update;

        VersionText.Text = $"Version {_update.Version} is available (you have {updateService.CurrentVersion}).";
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(_update.ReleaseNotes)
            ? "No release notes provided."
            : _update.ReleaseNotes;
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        LaterButton.IsEnabled = false;
        InstallButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        StatusText.Text = "Downloading update...";

        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(value => DownloadProgress.Value = value);
            var installerPath = await _updateService.DownloadInstallerAsync(_update, progress, _cts.Token);

            StatusText.Text = "Launching installer...";
            _updateService.LaunchInstaller(installerPath);

            System.Windows.Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Download cancelled.";
            LaterButton.IsEnabled = true;
            InstallButton.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Download failed.";
            System.Windows.MessageBox.Show(
                $"Could not download the update:\n\n{ex.Message}",
                "Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            LaterButton.IsEnabled = true;
            InstallButton.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
