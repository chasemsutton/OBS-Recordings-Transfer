using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using ODBC.RecordingsTransfer.Models;
using ODBC.RecordingsTransfer.Services;
using ODBC.RecordingsTransfer.Views;

namespace ODBC.RecordingsTransfer.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly LoggingService _loggingService;
    private readonly TransferService _transferService;
    private readonly UpdateService _updateService;
    private CancellationTokenSource? _autoCloseCts;

    private string _sourcePath = "";
    private string _destinationPath = "";
    private int _maxFileAgeDays = 90;
    private double _minFreeSpaceGb = 25;
    private int _autoCloseSeconds = 15;
    private bool _verifyTransfer;
    private bool _verifyRemux;
    private bool _autoRunOnStartup;
    private bool _checkForUpdatesOnStartup = true;
    private bool _isRunning;
    private bool _isCheckingForUpdates;
    private string _statusText = "Ready";
    private string _logText = "";
    private int _detectedCount;
    private int _movedCount;
    private int _deletedCount;
    private int _leftCount;

    public MainViewModel()
    {
        _configService = new ConfigService();
        _loggingService = new LoggingService();
        _transferService = new TransferService(_loggingService);
        _updateService = new UpdateService();
        _loggingService.LogMessage += message => AppendLog(message);

        AppVersion = $"v{_updateService.CurrentVersion}";

        FileActions = new ObservableCollection<string>();

        LoadSettings();

        RunTransferCommand = new RelayCommand(_ => _ = RunTransferAsync(), _ => !IsRunning);
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        BrowseSourceCommand = new RelayCommand(_ => BrowseFolder(path => SourcePath = path));
        BrowseDestinationCommand = new RelayCommand(_ => BrowseFolder(path => DestinationPath = path));
        ClearLogCommand = new RelayCommand(_ => LogText = "");
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        CancelAutoCloseCommand = new RelayCommand(_ => CancelAutoClose());
        CheckForUpdatesCommand = new RelayCommand(_ => _ = CheckForUpdatesAsync(manual: true), _ => !IsCheckingForUpdates);
    }

    public string AppVersion { get; }

    public ObservableCollection<string> FileActions { get; }

    public event Action? RequestClose;

    public string SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value);
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set => SetProperty(ref _destinationPath, value);
    }

    public int MaxFileAgeDays
    {
        get => _maxFileAgeDays;
        set => SetProperty(ref _maxFileAgeDays, value);
    }

    public double MinFreeSpaceGb
    {
        get => _minFreeSpaceGb;
        set => SetProperty(ref _minFreeSpaceGb, value);
    }

    public int AutoCloseSeconds
    {
        get => _autoCloseSeconds;
        set => SetProperty(ref _autoCloseSeconds, value);
    }

    public bool VerifyTransfer
    {
        get => _verifyTransfer;
        set => SetProperty(ref _verifyTransfer, value);
    }

    public bool VerifyRemux
    {
        get => _verifyRemux;
        set => SetProperty(ref _verifyRemux, value);
    }

    public bool AutoRunOnStartup
    {
        get => _autoRunOnStartup;
        set => SetProperty(ref _autoRunOnStartup, value);
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _checkForUpdatesOnStartup;
        set => SetProperty(ref _checkForUpdatesOnStartup, value);
    }

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        set
        {
            if (SetProperty(ref _isCheckingForUpdates, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string LogText
    {
        get => _logText;
        set => SetProperty(ref _logText, value);
    }

    public int DetectedCount
    {
        get => _detectedCount;
        set => SetProperty(ref _detectedCount, value);
    }

    public int MovedCount
    {
        get => _movedCount;
        set => SetProperty(ref _movedCount, value);
    }

    public int DeletedCount
    {
        get => _deletedCount;
        set => SetProperty(ref _deletedCount, value);
    }

    public int LeftCount
    {
        get => _leftCount;
        set => SetProperty(ref _leftCount, value);
    }

    public ICommand RunTransferCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand BrowseSourceCommand { get; }
    public ICommand BrowseDestinationCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand CancelAutoCloseCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }

    public async Task InitializeAsync()
    {
        if (!FFmpegService.IsAvailable())
            AppendLog("Note: FFmpeg not found. Remux validation will be skipped unless FFmpeg is installed.");

        if (CheckForUpdatesOnStartup)
            await CheckForUpdatesAsync(manual: false);

        if (AutoRunOnStartup)
            await RunTransferAsync();
    }

    private void LoadSettings()
    {
        var settings = _configService.Load();
        SourcePath = settings.SourcePath;
        DestinationPath = settings.DestinationPath;
        MaxFileAgeDays = settings.MaxFileAgeDays;
        MinFreeSpaceGb = settings.MinFreeSpaceGb;
        AutoCloseSeconds = settings.AutoCloseSeconds;
        VerifyTransfer = settings.VerifyTransfer;
        VerifyRemux = settings.VerifyRemux;
        AutoRunOnStartup = settings.AutoRunOnStartup;
        CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
    }

    private void SaveSettings()
    {
        _configService.Save(ToSettings());
        AppendLog("Settings saved.");
        StatusText = "Settings saved";
    }

    private AppSettings ToSettings() => new()
    {
        SourcePath = SourcePath,
        DestinationPath = DestinationPath,
        MaxFileAgeDays = MaxFileAgeDays,
        MinFreeSpaceGb = MinFreeSpaceGb,
        AutoCloseSeconds = AutoCloseSeconds,
        VerifyTransfer = VerifyTransfer,
        VerifyRemux = VerifyRemux,
        AutoRunOnStartup = AutoRunOnStartup,
        CheckForUpdatesOnStartup = CheckForUpdatesOnStartup
    };

    private async Task CheckForUpdatesAsync(bool manual)
    {
        IsCheckingForUpdates = true;
        if (manual)
            StatusText = "Checking for updates...";

        try
        {
            var update = await _updateService.CheckForUpdateAsync();

            if (update == null)
            {
                if (manual)
                {
                    StatusText = "You're up to date";
                    System.Windows.MessageBox.Show(
                        $"You're running the latest version ({_updateService.CurrentVersion}).",
                        "No Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

            var window = new UpdateWindow(_updateService, update)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            if (manual)
            {
                StatusText = "Update check failed";
                System.Windows.MessageBox.Show(
                    $"Could not check for updates:\n\n{ex.Message}",
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                AppendLog($"Update check failed: {ex.Message}");
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
            if (manual && StatusText.StartsWith("Checking", StringComparison.Ordinal))
                StatusText = "Ready";
        }
    }

    private async Task RunTransferAsync()
    {
        CancelAutoClose();
        IsRunning = true;
        StatusText = "Running transfer...";
        ResetCounts();
        FileActions.Clear();

        var settings = ToSettings();
        _configService.Save(settings);

        var progress = new Progress<string>(message =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AppendLog(message);
                if (message.StartsWith("Moved:", StringComparison.Ordinal))
                    FileActions.Add("✓ " + message[6..].Trim());
                else if (message.StartsWith("Deleted", StringComparison.Ordinal))
                    FileActions.Add("✗ " + message);
                else if (message.StartsWith("Skipped", StringComparison.Ordinal))
                    FileActions.Add("– " + message);
            });
        });

        var context = new TransferContext
        {
            ConfirmRetry = fileName =>
            {
                var result = MessageBoxResult.No;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    result = System.Windows.MessageBox.Show(
                        $"Transfer failed for:\n{fileName}\n\nTry again?",
                        "Transfer Error",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                });
                return result == MessageBoxResult.Yes;
            }
        };

        try
        {
            var result = await Task.Run(() => _transferService.Run(settings, context, progress));

            DetectedCount = result.Detected.Count;
            MovedCount = result.Moved.Count;
            DeletedCount = result.Deleted.Count;
            LeftCount = result.Left.Count;

            _loggingService.WriteResult(result);

            if (result.Success)
            {
                StatusText = $"Complete — {result.Moved.Count} moved, {result.Deleted.Count} deleted";
                AppendLog("Transfer complete.");
            }
            else
            {
                StatusText = $"Finished with {result.Errors.Count} error(s)";
                foreach (var error in result.Errors)
                    AppendLog($"ERROR: {error}");
            }

            if (AutoCloseSeconds > 0)
                _ = StartAutoCloseAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Transfer failed";
            AppendLog($"ERROR: {ex.Message}");
            ErrorLogService.Write(ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task StartAutoCloseAsync()
    {
        CancelAutoClose();
        _autoCloseCts = new CancellationTokenSource();
        var token = _autoCloseCts.Token;

        try
        {
            for (var i = AutoCloseSeconds; i > 0; i--)
            {
                StatusText = $"Closing in {i} second{(i == 1 ? "" : "s")}... (click status to cancel)";
                await Task.Delay(1000, token);
            }

            if (!token.IsCancellationRequested)
                RequestClose?.Invoke();
        }
        catch (TaskCanceledException)
        {
            StatusText = "Ready";
        }
    }

    private void CancelAutoClose()
    {
        _autoCloseCts?.Cancel();
        _autoCloseCts = null;
    }

    private void ResetCounts()
    {
        DetectedCount = 0;
        MovedCount = 0;
        DeletedCount = 0;
        LeftCount = 0;
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogText = string.IsNullOrEmpty(LogText) ? line : LogText + Environment.NewLine + line;
    }

    private static void BrowseFolder(Action<string> setPath)
    {
        using var dialog = new FolderBrowserDialog { Description = "Select folder" };
        if (dialog.ShowDialog() == DialogResult.OK)
            setPath(dialog.SelectedPath);
    }

    private static void OpenLogFolder()
    {
        var folder = AppContext.BaseDirectory;
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }
}
