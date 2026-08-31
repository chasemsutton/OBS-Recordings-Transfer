using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using OBS.RecordingsTransfer.Models;
using OBS.RecordingsTransfer.Services;

namespace OBS.RecordingsTransfer.Views;

public partial class UpdateWindow : Window
{
    private readonly UpdateService _updateService;
    private readonly IReadOnlyList<UpdateInfo> _releases;
    private UpdateInfo _selected;
    private CancellationTokenSource? _cts;

    public UpdateWindow(UpdateService updateService, UpdateInfo update)
        : this(updateService, new[] { update }, update)
    {
    }

    public UpdateWindow(
        UpdateService updateService,
        IReadOnlyList<UpdateInfo> releases,
        UpdateInfo? preferred = null)
    {
        InitializeComponent();
        _updateService = updateService;
        _releases = releases.Count > 0 ? releases : throw new ArgumentException("At least one release is required.", nameof(releases));
        _selected = preferred ?? _releases.FirstOrDefault(r => r.IsNewer) ?? _releases.FirstOrDefault(r => !r.IsCurrent) ?? _releases[0];

        var isBetaPicker = _releases.Count > 1 || _releases[0].Channel == UpdateChannel.Beta;
        if (isBetaPicker)
        {
            Title = "Beta Releases";
            TitleText.Text = "Beta Releases";
            VersionPickerPanel.Visibility = Visibility.Visible;
            VersionCombo.ItemsSource = _releases;
            VersionCombo.SelectedItem = _selected;
            VersionText.Text = $"You are running v{updateService.CurrentVersion}. Choose a compatible beta to install or roll back to.";
        }
        else
        {
            VersionText.Text =
                $"Version {_selected.Version} is available on the {_selected.Channel} channel (you have {updateService.CurrentVersion}).";
        }

        ApplySelectedRelease();
    }

    private void VersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionCombo.SelectedItem is UpdateInfo info)
        {
            _selected = info;
            ApplySelectedRelease();
        }
    }

    private void ApplySelectedRelease()
    {
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(_selected.ReleaseNotes)
            ? "No release notes provided."
            : StripReleaseMarkers(_selected.ReleaseNotes);

        if (_selected.IsCurrent)
        {
            InstallButton.IsEnabled = false;
            InstallButton.Content = "Already installed";
            StatusText.Text = "This is the version you are running.";
            StatusText.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        if (_selected.RequiresManualReinstall(_updateService.CurrentVersion))
        {
            InstallButton.IsEnabled = true;
            InstallButton.Content = "Open GitHub Releases";
            StatusText.Text =
                $"This version requires at least v{_selected.MinUpdateFrom} for in-app update. " +
                $"You are on v{_updateService.CurrentVersion}. Uninstall the current app, then download and install v{_selected.Version} from GitHub.";
            StatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
            return;
        }

        InstallButton.IsEnabled = true;
        InstallButton.Content = _selected.IsOlder ? "Download and Downgrade" : "Download and Install";
        StatusText.Text = "";
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
    }

    private static string StripReleaseMarkers(string notes)
    {
        notes = System.Text.RegularExpressions.Regex.Replace(
            notes,
            @"<!--\s*compat-min:\s*[0-9]+(?:\.[0-9]+){1,3}\s*-->",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        notes = System.Text.RegularExpressions.Regex.Replace(
            notes,
            @"<!--\s*update-from:\s*[0-9]+(?:\.[0-9]+){1,3}\s*-->",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return notes.Trim();
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected.IsCurrent)
            return;

        if (_selected.RequiresManualReinstall(_updateService.CurrentVersion))
        {
            _updateService.OpenReleasesPage();
            return;
        }

        if (_selected.IsOlder)
        {
            var confirm = System.Windows.MessageBox.Show(
                $"Install older beta v{_selected.Version}?\n\n" +
                "Settings unknown to that build may be dropped the next time it saves config. " +
                "Use this for testing rollbacks only.",
                "Confirm Downgrade",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;
        }

        LaterButton.IsEnabled = false;
        InstallButton.IsEnabled = false;
        VersionCombo.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StatusText.Text = _selected.IsOlder ? "Downloading older build..." : "Downloading update...";

        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(value => DownloadProgress.Value = value);
            var installerPath = await _updateService.DownloadInstallerAsync(_selected, progress, _cts.Token);

            StatusText.Text = "Launching installer...";
            _updateService.LaunchInstaller(installerPath);

            System.Windows.Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Download cancelled.";
            LaterButton.IsEnabled = true;
            VersionCombo.IsEnabled = true;
            ApplySelectedRelease();
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
            VersionCombo.IsEnabled = true;
            ApplySelectedRelease();
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
