using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
    private static readonly HashSet<string> AutoSaveProperties = new(StringComparer.Ordinal)
    {
        nameof(SourcePath),
        nameof(DestinationPath),
        nameof(MaxFileAgeDays),
        nameof(MinFreeSpaceGb),
        nameof(AutoCloseSeconds),
        nameof(VerifyTransfer),
        nameof(VerifyRemux),
        nameof(AutoRunOnStartup),
        nameof(AutoRunDelayText),
        nameof(CheckForUpdatesOnStartup),
        nameof(UpdateChannelName),
        nameof(SkipDestinationYearWarning)
    };

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
    private string _autoRunDelayText = "5";
    private bool _checkForUpdatesOnStartup = true;
    private string _updateChannelName = "Stable";
    private bool _skipDestinationYearWarning;
    private bool _isRunning;
    private bool _isCheckingForUpdates;
    private string _statusText = "Ready";
    private string _logText = "";
    private int _detectedCount;
    private int _movedCount;
    private int _deletedCount;
    private int _leftCount;
    private bool _isLoadingSettings;
    private CancellationTokenSource? _autoSaveCts;
    private bool _isAutoCloseCountdownActive;

    public MainViewModel()
    {
        _configService = new ConfigService();
        _loggingService = new LoggingService();
        _transferService = new TransferService(_loggingService);
        _updateService = new UpdateService();
        _loggingService.LogMessage += message => AppendLog(message);

        AppVersion = $"v{_updateService.CurrentVersion}";

        TransferItems = new ObservableCollection<TransferActionViewModel>();
        UpdateChannels = new ObservableCollection<string> { "Stable", "Beta" };

        PropertyChanged += (_, e) =>
        {
            if (_isLoadingSettings || e.PropertyName == null || !AutoSaveProperties.Contains(e.PropertyName))
                return;

            ScheduleAutoSave();
        };

        LoadSettings();

        RunTransferCommand = new RelayCommand(_ => _ = RunTransferAsync(), _ => !IsRunning);
        BrowseSourceCommand = new RelayCommand(_ => BrowseFolder(path => SourcePath = path));
        BrowseDestinationCommand = new RelayCommand(_ => BrowseFolder(path => DestinationPath = path));
        ClearLogCommand = new RelayCommand(_ => LogText = "");
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        CancelAutoCloseCommand = new RelayCommand(_ => CancelAutoClose());
        CheckForUpdatesCommand = new RelayCommand(_ => _ = CheckForUpdatesAsync(manual: true), _ => !IsCheckingForUpdates);
    }

    public string AppVersion { get; }
    public ObservableCollection<TransferActionViewModel> TransferItems { get; }
    public ObservableCollection<string> UpdateChannels { get; }

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

    public string AutoRunDelayText
    {
        get => _autoRunDelayText;
        set => SetProperty(ref _autoRunDelayText, value);
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _checkForUpdatesOnStartup;
        set => SetProperty(ref _checkForUpdatesOnStartup, value);
    }

    public string UpdateChannelName
    {
        get => _updateChannelName;
        set => SetProperty(ref _updateChannelName, value);
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

    public bool IsAutoCloseCountdownActive
    {
        get => _isAutoCloseCountdownActive;
        set => SetProperty(ref _isAutoCloseCountdownActive, value);
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
        {
            var delay = ParseAutoRunDelay();
            if (delay > 0)
            {
                StatusText = $"Auto-starting transfer in {delay} second{(delay == 1 ? "" : "s")}...";
                AppendLog($"Auto-start transfer scheduled in {delay} second{(delay == 1 ? "" : "s")}.");
                await Task.Delay(TimeSpan.FromSeconds(delay));
            }

            await RunTransferAsync();
        }
    }

    private void LoadSettings()
    {
        _isLoadingSettings = true;
        try
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
            AutoRunDelayText = settings.AutoRunDelaySeconds.ToString();
            CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
            UpdateChannelName = settings.UpdateChannel.ToString();
            SkipDestinationYearWarning = settings.SkipDestinationYearWarning;
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    public bool SkipDestinationYearWarning
    {
        get => _skipDestinationYearWarning;
        set => SetProperty(ref _skipDestinationYearWarning, value);
    }

    private void ScheduleAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, token);
                if (token.IsCancellationRequested)
                    return;

                System.Windows.Application.Current.Dispatcher.Invoke(AutoSaveSettings);
            }
            catch (TaskCanceledException)
            {
                // expected when another change arrives before the delay finishes
            }
        }, token);
    }

    private void AutoSaveSettings()
    {
        try
        {
            _configService.Save(ToSettings());
        }
        catch (Exception ex)
        {
            AppendLog($"Could not save settings: {ex.Message}");
            ErrorLogService.Write(ex);
        }
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
        AutoRunDelaySeconds = ParseAutoRunDelay(),
        CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
        UpdateChannel = ParseUpdateChannel(),
        SkipDestinationYearWarning = SkipDestinationYearWarning
    };

    private int ParseAutoRunDelay()
    {
        return string.IsNullOrWhiteSpace(AutoRunDelayText) || !int.TryParse(AutoRunDelayText, out var seconds) || seconds < 0
            ? 5
            : seconds;
    }

    private UpdateChannel ParseUpdateChannel()
    {
        return Enum.TryParse<UpdateChannel>(UpdateChannelName, true, out var channel)
            ? channel
            : UpdateChannel.Stable;
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        IsCheckingForUpdates = true;
        var channel = ParseUpdateChannel();

        if (manual)
            StatusText = $"Checking for {channel.ToString().ToLower()} updates...";

        try
        {
            var result = await _updateService.CheckForUpdateAsync(channel);

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                StatusText = "Update check failed";
                AppendLog($"Update check failed: {result.ErrorMessage}");
                if (manual)
                {
                    System.Windows.MessageBox.Show(
                        $"Could not check for updates:\n\n{result.ErrorMessage}",
                        "Update Check Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            if (!result.UpdateAvailable || result.Update == null)
            {
                if (manual)
                {
                    StatusText = "You're up to date";
                    System.Windows.MessageBox.Show(
                        $"You're running the latest {channel.ToString().ToLower()} version ({_updateService.CurrentVersion}).",
                        "No Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

            var update = result.Update;
            StatusText = $"Update available: v{update.Version} ({channel})";
            AppendLog($"Update available on {channel} channel: v{update.Version}");

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

    private bool ConfirmDestinationPath()
    {
        if (SkipDestinationYearWarning)
            return true;

        if (!DestinationPathValidator.TryGetFolderYear(DestinationPath, out var folderYear))
            return true;

        var currentYear = DateTime.Now.Year;
        if (folderYear == currentYear)
            return true;

        var owner = System.Windows.Application.Current.MainWindow;
        var dialog = new ConfirmDestinationWindow(DestinationPath, folderYear, currentYear)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() != true)
            return false;

        if (dialog.DontAskAgain)
            SkipDestinationYearWarning = true;

        if (dialog.Result == DestinationConfirmResult.UpdateYear)
        {
            DestinationPath = DestinationPathValidator.UpdateYearInPath(DestinationPath, currentYear);
            AppendLog($"Destination path year updated to {currentYear}: {DestinationPath}");
        }

        return dialog.Result != DestinationConfirmResult.Cancel;
    }

    private async Task RunTransferAsync()
    {
        if (!ConfirmDestinationPath())
        {
            StatusText = "Transfer cancelled";
            AppendLog("Transfer cancelled by user (destination year warning).");
            return;
        }

        CancelAutoClose();
        IsRunning = true;
        StatusText = "Running transfer...";
        ResetCounts();
        TransferItems.Clear();

        var settings = ToSettings();
        _configService.Save(settings);

        var plan = _transferService.BuildPlan(settings);
        var itemLookup = new Dictionary<string, TransferActionViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var planItem in plan)
        {
            var vm = new TransferActionViewModel(planItem);
            TransferItems.Add(vm);
            itemLookup[planItem.FileName] = vm;
            AppendLog($"Planned: {planItem.Description}");
        }

        if (plan.Count == 0)
            AppendLog("No file actions planned.");

        var progress = new Progress<TransferProgressUpdate>(update =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => HandleTransferUpdate(update, itemLookup));
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

    private void HandleTransferUpdate(TransferProgressUpdate update, Dictionary<string, TransferActionViewModel> itemLookup)
    {
        if (update.Kind == TransferProgressUpdateKind.Log && !string.IsNullOrWhiteSpace(update.Message))
        {
            AppendLog(update.Message);
            return;
        }

        if (update.Kind == TransferProgressUpdateKind.Plan)
        {
            if (!string.IsNullOrWhiteSpace(update.Message))
                AppendLog($"Planned: {update.Message}");
            return;
        }

        if (!itemLookup.TryGetValue(update.FileName, out var item))
            return;

        switch (update.Kind)
        {
            case TransferProgressUpdateKind.Start:
                item.Status = TransferActionStatus.InProgress;
                item.Progress = 0;
                if (update.TotalBytes > 0)
                    item.ProgressText = FileSizeFormatter.FormatProgress(0, update.TotalBytes);
                if (!string.IsNullOrWhiteSpace(update.Message))
                    AppendLog(update.Message);
                break;

            case TransferProgressUpdateKind.Progress:
                item.Status = TransferActionStatus.InProgress;
                item.Progress = update.Progress;
                if (update.TotalBytes > 0)
                    item.ProgressText = FileSizeFormatter.FormatProgress(update.BytesTransferred, update.TotalBytes);
                break;

            case TransferProgressUpdateKind.Complete:
                item.Status = TransferActionStatus.Complete;
                item.Progress = 1;
                item.ProgressText = "";
                if (!string.IsNullOrWhiteSpace(update.Message))
                    AppendLog(update.Message);
                break;

            case TransferProgressUpdateKind.Skipped:
                item.Status = TransferActionStatus.Skipped;
                if (!string.IsNullOrWhiteSpace(update.Message))
                    AppendLog(update.Message);
                break;

            case TransferProgressUpdateKind.Failed:
                item.Status = TransferActionStatus.Failed;
                if (!string.IsNullOrWhiteSpace(update.Message))
                    AppendLog(update.Message);
                break;
        }
    }

    private async Task StartAutoCloseAsync()
    {
        CancelAutoClose();
        _autoCloseCts = new CancellationTokenSource();
        var token = _autoCloseCts.Token;
        IsAutoCloseCountdownActive = true;

        try
        {
            for (var i = AutoCloseSeconds; i > 0; i--)
            {
                StatusText = $"Closing in {i} second{(i == 1 ? "" : "s")}...";
                await Task.Delay(1000, token);
            }

            if (!token.IsCancellationRequested)
                RequestClose?.Invoke();
        }
        catch (TaskCanceledException)
        {
            StatusText = "Ready";
        }
        finally
        {
            IsAutoCloseCountdownActive = false;
        }
    }

    private void CancelAutoClose()
    {
        if (_autoCloseCts == null)
            return;

        _autoCloseCts.Cancel();
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
        AppPaths.EnsureAppDataDirectory();
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.AppDataDirectory) { UseShellExecute = true });
    }
}
