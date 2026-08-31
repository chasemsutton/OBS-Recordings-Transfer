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
        nameof(CheckRemuxComplete),
        nameof(TransferMode),
        nameof(AutoRunDelayText),
        nameof(StartWithWindows),
        nameof(StartMinimized),
        nameof(CheckForUpdatesOnStartup),
        nameof(UpdateChannelName),
        nameof(SkipDestinationYearWarning),
        nameof(ShowSettingsPanel)
    };

    private static readonly HashSet<string> QueueRefreshProperties = new(StringComparer.Ordinal)
    {
        nameof(SourcePath),
        nameof(DestinationPath),
        nameof(MaxFileAgeDays),
        nameof(MinFreeSpaceGb),
        nameof(CheckRemuxComplete)
    };

    private readonly ConfigService _configService;
    private readonly LoggingService _loggingService;
    private readonly TransferService _transferService;
    private readonly UpdateService _updateService;
    private CancellationTokenSource? _autoCloseCts;
    private CancellationTokenSource? _autoStartCts;
    private CancellationTokenSource? _transferCts;
    private CancellationTokenSource? _continuousCts;
    private CancellationTokenSource? _queueRefreshCts;
    private CancellationTokenSource? _queueRefreshDebounceCts;
    private bool _isClosing;
    private int _queueRefreshRunning;
    private const int QueueRefreshIntervalMs = 3000;
    private const int ContinuousPollIntervalMs = 3000;

    private string _sourcePath = "";
    private string _destinationPath = "";
    private int _maxFileAgeDays = 90;
    private double _minFreeSpaceGb = 25;
    private int _autoCloseSeconds = 15;
    private bool _verifyTransfer;
    private bool _verifyRemux;
    private bool _checkRemuxComplete = true;
    private TransferMode _transferMode = TransferMode.None;
    private string _autoRunDelayText = "5";
    private bool _startWithWindows;
    private bool _startMinimized;
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
    private bool _showSettingsPanel = true;
    private bool _isAutoStartCountdownActive;
    private string _autoStartCountdownText = "";
    private double _autoStartCountdownProgress;

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
            if (_isLoadingSettings || e.PropertyName == null)
                return;

            if (AutoSaveProperties.Contains(e.PropertyName))
                ScheduleAutoSave();

            if (QueueRefreshProperties.Contains(e.PropertyName))
                ScheduleQueueRefresh();
        };

        LoadSettings();

        RunTransferCommand = new RelayCommand(_ => _ = RunTransferAsync(), _ => !IsRunning && !IsAutoStartCountdownActive && TransferMode != TransferMode.Continuous);
        TransferPrimaryCommand = new RelayCommand(
            _ =>
            {
                if (IsAutoCloseCountdownActive)
                    CancelAutoClose(keepOpen: true);
                else if (IsAutoStartCountdownActive)
                    CancelAutoStart();
                else if (ShowStopTransferButton)
                    StopTransfer(resetContinuousToNone: IsContinuousMode);
                else
                    _ = RunTransferAsync();
            },
            _ => true);
        BrowseSourceCommand = new RelayCommand(_ => BrowseFolder(path => SourcePath = path));
        BrowseDestinationCommand = new RelayCommand(_ => BrowseFolder(path => DestinationPath = path));
        ClearLogCommand = new RelayCommand(_ => LogText = "");
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        CheckForUpdatesCommand = new RelayCommand(_ => _ = CheckForUpdatesAsync(manual: true), _ => !IsCheckingForUpdates);
        ToggleSettingsPanelCommand = new RelayCommand(_ => ShowSettingsPanel = !ShowSettingsPanel);
    }

    public event Action<bool>? SettingsPanelVisibilityChanged;

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

    public bool CheckRemuxComplete
    {
        get => _checkRemuxComplete;
        set
        {
            if (SetProperty(ref _checkRemuxComplete, value))
                _transferService.RemuxTracker.InvalidateCache();
        }
    }

    public TransferMode TransferMode
    {
        get => _transferMode;
        set
        {
            var previous = _transferMode;
            if (!SetProperty(ref _transferMode, value))
                return;

            OnPropertyChanged(nameof(IsTransferModeNone));
            OnPropertyChanged(nameof(IsTransferModeAutoStart));
            OnPropertyChanged(nameof(IsTransferModeContinuous));
            OnPropertyChanged(nameof(IsContinuousMode));
            OnPropertyChanged(nameof(ShowStopTransferButton));
            CommandManager.InvalidateRequerySuggested();

            if (_isLoadingSettings)
                return;

            if (value == TransferMode.Continuous)
            {
                StartContinuousMode();
            }
            else
            {
                StopContinuousLoopOnly();
                if (previous == TransferMode.Continuous)
                {
                    _transferCts?.Cancel();
                    if (!IsRunning && !_isClosing)
                        StatusText = "Ready";
                }
            }
        }
    }

    public bool IsTransferModeNone
    {
        get => TransferMode == TransferMode.None;
        set { if (value) TransferMode = TransferMode.None; }
    }

    public bool IsTransferModeAutoStart
    {
        get => TransferMode == TransferMode.AutoStart;
        set { if (value) TransferMode = TransferMode.AutoStart; }
    }

    public bool IsTransferModeContinuous
    {
        get => TransferMode == TransferMode.Continuous;
        set { if (value) TransferMode = TransferMode.Continuous; }
    }

    public bool IsContinuousMode => TransferMode == TransferMode.Continuous;

    public bool ShowStopTransferButton =>
        (IsRunning || IsContinuousMode)
        && !IsAutoStartCountdownActive
        && !IsAutoCloseCountdownActive;

    public string AutoRunDelayText
    {
        get => _autoRunDelayText;
        set => SetProperty(ref _autoRunDelayText, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetProperty(ref _startMinimized, value);
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
            {
                OnPropertyChanged(nameof(ShowStopTransferButton));
                CommandManager.InvalidateRequerySuggested();
            }
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
        set
        {
            if (SetProperty(ref _isAutoCloseCountdownActive, value))
            {
                OnPropertyChanged(nameof(TransferPrimaryButtonText));
                OnPropertyChanged(nameof(ShowStopTransferButton));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool ShowSettingsPanel
    {
        get => _showSettingsPanel;
        set
        {
            if (SetProperty(ref _showSettingsPanel, value))
            {
                OnPropertyChanged(nameof(SettingsToggleButtonText));
                SettingsPanelVisibilityChanged?.Invoke(value);
            }
        }
    }

    public string SettingsToggleButtonText => ShowSettingsPanel ? "Hide Settings" : "Show Settings";

    public bool IsAutoStartCountdownActive
    {
        get => _isAutoStartCountdownActive;
        set
        {
            if (SetProperty(ref _isAutoStartCountdownActive, value))
            {
                OnPropertyChanged(nameof(TransferPrimaryButtonText));
                OnPropertyChanged(nameof(ShowStopTransferButton));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string AutoStartCountdownText
    {
        get => _autoStartCountdownText;
        set => SetProperty(ref _autoStartCountdownText, value);
    }

    public double AutoStartCountdownProgress
    {
        get => _autoStartCountdownProgress;
        set => SetProperty(ref _autoStartCountdownProgress, value);
    }

    public string TransferPrimaryButtonText =>
        IsAutoCloseCountdownActive ? "Cancel (keep open)"
        : IsAutoStartCountdownActive ? "Cancel"
        : "Run Transfer";

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
    public ICommand TransferPrimaryCommand { get; }
    public ICommand BrowseSourceCommand { get; }
    public ICommand BrowseDestinationCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand ToggleSettingsPanelCommand { get; }

    public async Task InitializeAsync()
    {
        if (!FFmpegService.IsAvailable())
            AppendLog("Note: FFmpeg not found. Remux validation will be skipped unless FFmpeg is installed.");

        await RefreshTransferQueueAsync(logToActivity: false);
        StartQueueRefreshLoop();

        if (CheckForUpdatesOnStartup)
            await CheckForUpdatesAsync(manual: false);

        if (TransferMode == TransferMode.Continuous)
        {
            if (_isClosing)
                return;

            var delay = ParseAutoRunDelay();
            if (delay > 0)
            {
                if (!await StartAutoStartCountdownAsync(delay))
                    return;
            }

            if (_isClosing || TransferMode != TransferMode.Continuous)
                return;

            StartContinuousMode();
            return;
        }

        if (TransferMode == TransferMode.AutoStart)
        {
            if (_isClosing)
                return;

            var delay = ParseAutoRunDelay();
            if (delay > 0)
            {
                if (!await StartAutoStartCountdownAsync(delay))
                    return;
            }

            if (_isClosing)
                return;

            await RunTransferAsync();
        }
    }

    private void StartQueueRefreshLoop()
    {
        _queueRefreshCts?.Cancel();
        _queueRefreshCts = new CancellationTokenSource();
        var token = _queueRefreshCts.Token;
        _ = RunQueueRefreshLoopAsync(token);
    }

    private async Task RunQueueRefreshLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(QueueRefreshIntervalMs, token);
                if (_isClosing)
                    continue;

                try
                {
                    await RefreshTransferQueueAsync(logToActivity: false);
                }
                catch
                {
                    // Keep polling even if one scan fails (missing path, network share, etc.).
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on close
        }
    }

    private void ScheduleQueueRefresh()
    {
        _queueRefreshDebounceCts?.Cancel();
        _queueRefreshDebounceCts = new CancellationTokenSource();
        var token = _queueRefreshDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, token);
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || token.IsCancellationRequested || _isClosing)
                    return;

                await dispatcher.InvokeAsync(() => RefreshTransferQueueAsync(logToActivity: false));
            }
            catch (OperationCanceledException)
            {
                // expected when another change arrives before the delay finishes
            }
            catch
            {
                // Ignore dispatcher/shutdown races while typing a path.
            }
        }, token);
    }

    private async Task RefreshTransferQueueAsync(bool logToActivity)
    {
        if (_isClosing)
            return;

        if (Interlocked.CompareExchange(ref _queueRefreshRunning, 1, 0) != 0)
            return;

        try
        {
            var settings = ToSettings();
            List<TransferActionPlan> plan;
            try
            {
                plan = await Task.Run(() => _transferService.BuildPlan(settings));
            }
            catch (Exception ex)
            {
                if (logToActivity)
                    AppendLog($"Could not refresh transfer queue: {ex.Message}");
                return;
            }

            if (_isClosing)
                return;

            // While a transfer is running, merge the plan into the queue without wiping
            // in-progress progress or completed history (and add newly appeared files).
            if (IsRunning)
                MergeQueueFromPlanWhileRunning(plan);
            else
                ApplyPlanToQueue(plan, logToActivity);

            ShowRemuxIncompleteAlerts();
        }
        finally
        {
            Interlocked.Exchange(ref _queueRefreshRunning, 0);
        }
    }

    private void ShowRemuxIncompleteAlerts()
    {
        foreach (var fileName in _transferService.RemuxTracker.ConsumeIncompleteAlerts())
        {
            AppendLog($"Remux index (moov) not found yet — still waiting: {fileName}");
            System.Windows.MessageBox.Show(
                $"\"{fileName}\" has had a stable size for a while, but no remux index (moov) has been detected yet.\n\n" +
                "It will stay as waiting for successful remux. Other ready files will transfer first.",
                "Remux Not Ready Yet",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private List<TransferActionPlan> BuildTransferQueue(bool logToActivity)
    {
        var plan = _transferService.BuildPlan(ToSettings());
        ApplyPlanToQueue(plan, logToActivity);
        return plan;
    }

    private void ApplyPlanToQueue(List<TransferActionPlan> plan, bool logToActivity)
    {
        plan = OrderPlanForAction(plan);

        var history = TransferItems
            .Where(i => i.IsHistoryItem)
            .OrderByDescending(i => i.CompletedAt ?? DateTime.MinValue)
            .ToList();

        var active = TransferItems
            .Where(i => !i.IsHistoryItem)
            .ToList();

        if (!logToActivity && ActivePlansMatch(active, plan))
            return;

        var planNames = new HashSet<string>(
            plan.Select(p => p.FileName),
            StringComparer.OrdinalIgnoreCase);

        TransferItems.Clear();

        foreach (var planItem in plan)
        {
            TransferItems.Add(new TransferActionViewModel(planItem));
            if (logToActivity)
                AppendLog($"Planned: {planItem.Description}");
        }

        foreach (var item in history)
        {
            // Keep finished work under new files; drop if the same name is active again.
            if (planNames.Contains(item.FileName))
                continue;
            TransferItems.Add(item);
        }

        if (plan.Count == 0 && logToActivity)
            AppendLog("No file actions planned.");
    }

    /// <summary>
    /// While a transfer is running: update pending rows, revive remux skips, and insert
    /// newly detected files — without resetting in-progress progress or history.
    /// Active rows follow transfer order; completed history is newest-first under them.
    /// </summary>
    private void MergeQueueFromPlanWhileRunning(List<TransferActionPlan> plan)
    {
        plan = OrderPlanForAction(plan);

        var planNames = new HashSet<string>(
            plan.Select(p => p.FileName),
            StringComparer.OrdinalIgnoreCase);

        var reusable = TransferItems
            .Where(i => !i.IsHistoryItem || i.Status == TransferActionStatus.Skipped)
            .GroupBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(i => i.Status == TransferActionStatus.InProgress)
                    .ThenByDescending(i => i.Status == TransferActionStatus.Pending)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        var history = TransferItems
            .Where(i => i.IsHistoryItem && i.Status != TransferActionStatus.Skipped)
            .OrderByDescending(i => i.CompletedAt ?? DateTime.MinValue)
            .ToList();

        var inProgressOrphans = TransferItems
            .Where(i => i.Status == TransferActionStatus.InProgress && !planNames.Contains(i.FileName))
            .ToList();

        var nextActive = new List<TransferActionViewModel>();

        foreach (var planItem in plan)
        {
            if (reusable.TryGetValue(planItem.FileName, out var existing))
            {
                if (existing.Status == TransferActionStatus.InProgress)
                {
                    if (!nextActive.Contains(existing))
                        nextActive.Add(existing);
                    continue;
                }

                if (existing.Status == TransferActionStatus.Skipped)
                {
                    existing.Status = TransferActionStatus.Pending;
                    existing.CompletedAt = null;
                    existing.Progress = 0;
                    existing.ProgressText = "";
                }

                if (existing.Status == TransferActionStatus.Pending)
                {
                    existing.ActionType = planItem.ActionType;
                    existing.Label = planItem.Description;
                    if (!nextActive.Contains(existing))
                        nextActive.Add(existing);
                    continue;
                }
            }

            nextActive.Add(new TransferActionViewModel(planItem));
        }

        // Current work not in the plan anymore (e.g. mid-copy) stays among active tasks.
        foreach (var orphan in inProgressOrphans)
        {
            if (!nextActive.Contains(orphan))
                nextActive.Insert(0, orphan);
        }

        TransferItems.Clear();
        foreach (var item in nextActive)
            TransferItems.Add(item);

        foreach (var item in history)
        {
            if (planNames.Contains(item.FileName))
                continue;
            TransferItems.Add(item);
        }
    }

    /// <summary>
    /// Matches transfer execution order: destination skips, ready moves, remux waits, then deletes.
    /// </summary>
    private static List<TransferActionPlan> OrderPlanForAction(IReadOnlyList<TransferActionPlan> plan) =>
        plan.OrderBy(p => p.ActionType switch
        {
            TransferActionType.Skip => 0,
            TransferActionType.Move => 1,
            TransferActionType.WaitingRemux => 2,
            TransferActionType.Delete => 3,
            _ => 4
        }).ToList();

    /// <summary>
    /// Moves a just-finished item below all upcoming work and above older completed items.
    /// </summary>
    private void MoveFinishedItemToHistoryTop(TransferActionViewModel item)
    {
        if (!TransferItems.Contains(item))
            return;

        TransferItems.Remove(item);

        var insertAt = 0;
        while (insertAt < TransferItems.Count && !TransferItems[insertAt].IsHistoryItem)
            insertAt++;

        TransferItems.Insert(insertAt, item);
    }

    private static bool ActivePlansMatch(IReadOnlyList<TransferActionViewModel> active, List<TransferActionPlan> plan)
    {
        if (active.Count != plan.Count)
            return false;

        for (var i = 0; i < plan.Count; i++)
        {
            var existing = active[i];
            var item = plan[i];
            if (!string.Equals(existing.FileName, item.FileName, StringComparison.OrdinalIgnoreCase)
                || existing.ActionType != item.ActionType
                || existing.Label != item.Description)
                return false;
        }

        return true;
    }

    private async Task<bool> StartAutoStartCountdownAsync(int totalSeconds)
    {
        CancelAutoStart();
        _autoStartCts = new CancellationTokenSource();
        var token = _autoStartCts.Token;
        IsAutoStartCountdownActive = true;
        AutoStartCountdownProgress = 0;

        try
        {
            for (var remaining = totalSeconds; remaining > 0; remaining--)
            {
                AutoStartCountdownText = $"Transfer starting in {remaining} second{(remaining == 1 ? "" : "s")}...";
                AutoStartCountdownProgress = (totalSeconds - remaining) / (double)totalSeconds;
                await Task.Delay(1000, token);
            }

            AutoStartCountdownProgress = 1;
            return true;
        }
        catch (TaskCanceledException)
        {
            if (TransferMode == TransferMode.Continuous)
            {
                AppendLog("Continuous mode startup cancelled.");
                StopContinuousMode();
            }
            else
            {
                StatusText = "Auto-start cancelled";
                AppendLog("Auto-start transfer cancelled.");
            }
            return false;
        }
        finally
        {
            IsAutoStartCountdownActive = false;
            AutoStartCountdownText = "";
            AutoStartCountdownProgress = 0;
        }
    }

    private void CancelAutoStart()
    {
        if (_autoStartCts == null)
            return;

        _autoStartCts.Cancel();
        _autoStartCts = null;
    }

    public void PrepareForClose()
    {
        _isClosing = true;
        CancelAutoStart();
        CancelAutoClose();
        StopContinuousLoopOnly();
        _transferCts?.Cancel();
        _autoSaveCts?.Cancel();
        _queueRefreshCts?.Cancel();
        _queueRefreshDebounceCts?.Cancel();
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
            CheckRemuxComplete = settings.CheckRemuxComplete;
            TransferMode = settings.TransferMode;
            AutoRunDelayText = settings.AutoRunDelaySeconds.ToString();
            StartWithWindows = settings.StartWithWindows;
            StartMinimized = settings.StartMinimized;
            CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
            UpdateChannelName = settings.UpdateChannel.ToString();
            SkipDestinationYearWarning = settings.SkipDestinationYearWarning;
            ShowSettingsPanel = settings.ShowSettingsPanel;
        }
        finally
        {
            _isLoadingSettings = false;
        }

        WindowsStartupService.Apply(StartWithWindows);
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
            var settings = ToSettings();
            _configService.Save(settings);
            WindowsStartupService.Apply(settings.StartWithWindows);
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
        CheckRemuxComplete = CheckRemuxComplete,
        TransferMode = TransferMode,
        AutoRunDelaySeconds = ParseAutoRunDelay(),
        StartWithWindows = StartWithWindows,
        StartMinimized = StartMinimized,
        CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
        UpdateChannel = ParseUpdateChannel(),
        SkipDestinationYearWarning = SkipDestinationYearWarning,
        ShowSettingsPanel = ShowSettingsPanel
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
            var includeOlderBetas = manual && channel == UpdateChannel.Beta;
            var result = await _updateService.CheckForUpdateAsync(channel, includeOlderBetas);

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

            if (channel == UpdateChannel.Beta && result.CompatibleReleases.Count > 0)
            {
                if (!manual && !result.CompatibleReleases.Any(r => r.IsNewer))
                    return;

                var preferred = result.Update
                    ?? result.CompatibleReleases.FirstOrDefault(r => r.IsNewer)
                    ?? result.CompatibleReleases.FirstOrDefault(r => r.IsCurrent)
                    ?? result.CompatibleReleases[0];

                StatusText = preferred.IsNewer
                    ? $"Update available: v{preferred.Version} ({channel})"
                    : "Select a beta version";
                AppendLog($"Opened beta version picker ({result.CompatibleReleases.Count} compatible release(s)).");

                var betaWindow = new UpdateWindow(_updateService, result.CompatibleReleases, preferred)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                betaWindow.ShowDialog();
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

    private async Task RunTransferAsync(bool fromContinuousLoop = false)
    {
        if (_isClosing)
            return;

        if (IsRunning)
            return;

        if (!ConfirmDestinationPath())
        {
            StatusText = "Transfer cancelled";
            AppendLog("Transfer cancelled by user (destination year warning).");
            if (fromContinuousLoop)
                StopContinuousMode();
            return;
        }

        if (_isClosing)
            return;

        CancelAutoClose();
        if (!fromContinuousLoop)
            CancelAutoStart();

        _transferCts?.Cancel();
        _transferCts = new CancellationTokenSource();
        var transferToken = _transferCts.Token;

        IsRunning = true;
        StatusText = fromContinuousLoop ? "Continuous transfer..." : "Running transfer...";
        if (!fromContinuousLoop)
            ResetCounts();

        var settings = ToSettings();
        _configService.Save(settings);

        AppendLog(fromContinuousLoop ? "Continuous mode: checking for ready files..." : "Refreshing transfer plan before run...");
        BuildTransferQueue(logToActivity: !fromContinuousLoop);
        var itemLookup = new Dictionary<string, TransferActionViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in TransferItems)
            itemLookup[item.FileName] = item;

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        var progress = new Progress<TransferProgressUpdate>(update =>
        {
            if (dispatcher.CheckAccess())
                HandleTransferUpdate(update, itemLookup);
            else
                dispatcher.BeginInvoke(() => HandleTransferUpdate(update, itemLookup));
        });

        var shouldAutoClose = false;
        var context = new TransferContext
        {
            CancellationToken = transferToken,
            ConfirmRetry = fileName =>
            {
                if (transferToken.IsCancellationRequested || fromContinuousLoop)
                    return false;

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
            },
            NotifyRemuxIncomplete = fileName =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AppendLog($"Remux index (moov) not found yet — still waiting: {fileName}");
                    if (!fromContinuousLoop)
                    {
                        System.Windows.MessageBox.Show(
                            $"\"{fileName}\" has had a stable size for a while, but no remux index (moov) has been detected yet.\n\n" +
                            "It will stay as waiting for successful remux. Other ready files will transfer first.",
                            "Remux Not Ready Yet",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                });
            }
        };

        try
        {
            var result = await Task.Run(() => _transferService.Run(settings, context, progress), transferToken);

            DetectedCount = result.Detected.Count;
            MovedCount = result.Moved.Count;
            DeletedCount = result.Deleted.Count;
            LeftCount = result.Left.Count;

            if (!transferToken.IsCancellationRequested)
                _loggingService.WriteResult(result);

            if (transferToken.IsCancellationRequested)
            {
                StatusText = "Transfer stopped";
                AppendLog("Transfer stopped.");
            }
            else if (result.Success)
            {
                StatusText = fromContinuousLoop
                    ? $"Watching for files — last run: {result.Moved.Count} moved"
                    : $"Complete — {result.Moved.Count} moved, {result.Deleted.Count} deleted";
                if (!fromContinuousLoop || result.Moved.Count > 0 || result.Deleted.Count > 0)
                    AppendLog(fromContinuousLoop ? "Continuous transfer pass complete." : "Transfer complete.");
            }
            else
            {
                StatusText = $"Finished with {result.Errors.Count} error(s)";
                foreach (var error in result.Errors)
                    AppendLog($"ERROR: {error}");
            }

            shouldAutoClose = !fromContinuousLoop && !_isClosing && AutoCloseSeconds > 0 && !transferToken.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Transfer stopped";
            AppendLog("Transfer stopped.");
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

        if (shouldAutoClose)
            _ = StartAutoCloseAsync();
    }

    private void StopTransfer(bool resetContinuousToNone)
    {
        _transferCts?.Cancel();
        if (resetContinuousToNone)
            StopContinuousMode();
        else if (!_isClosing)
            StatusText = "Stopping transfer...";
    }

    private void StartContinuousMode()
    {
        StopContinuousLoopOnly();
        CancelAutoClose();

        _continuousCts = new CancellationTokenSource();
        var token = _continuousCts.Token;
        StatusText = "Continuous transfer mode";
        AppendLog("Continuous auto-transfer mode started.");
        OnPropertyChanged(nameof(ShowStopTransferButton));
        CommandManager.InvalidateRequerySuggested();
        _ = RunContinuousLoopAsync(token);
    }

    private void StopContinuousMode()
    {
        AppendLog("Continuous auto-transfer mode stopped.");
        StopContinuousLoopOnly();
        _transferCts?.Cancel();

        if (TransferMode != TransferMode.None)
        {
            _isLoadingSettings = true;
            try
            {
                // Avoid re-entering StartContinuousMode via the setter.
                _transferMode = TransferMode.None;
                OnPropertyChanged(nameof(TransferMode));
                OnPropertyChanged(nameof(IsTransferModeNone));
                OnPropertyChanged(nameof(IsTransferModeAutoStart));
                OnPropertyChanged(nameof(IsTransferModeContinuous));
                OnPropertyChanged(nameof(IsContinuousMode));
                OnPropertyChanged(nameof(ShowStopTransferButton));
            }
            finally
            {
                _isLoadingSettings = false;
            }

            ScheduleAutoSave();
            CommandManager.InvalidateRequerySuggested();
        }

        if (!_isClosing && !IsRunning)
            StatusText = "Ready";
    }

    private void StopContinuousLoopOnly()
    {
        _continuousCts?.Cancel();
        _continuousCts = null;
    }

    private async Task RunContinuousLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && !_isClosing)
            {
                if (!IsRunning)
                {
                    await RefreshTransferQueueAsync(logToActivity: false);
                    if (token.IsCancellationRequested)
                        break;

                    if (HasActionableTransferWork())
                        await RunTransferAsync(fromContinuousLoop: true);
                    else if (!_isClosing)
                        StatusText = "Continuous mode — waiting for ready files...";
                }

                await Task.Delay(ContinuousPollIntervalMs, token);
            }
        }
        catch (OperationCanceledException)
        {
            // expected when stopping continuous mode
        }
    }

    private bool HasActionableTransferWork()
    {
        return TransferItems.Any(i =>
            !i.IsHistoryItem
            && i.ActionType is TransferActionType.Move or TransferActionType.WaitingRemux or TransferActionType.Delete);
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
                item.ActionType = update.ActionType;
                item.Status = TransferActionStatus.InProgress;
                item.Progress = 0;
                if (!string.IsNullOrWhiteSpace(update.Message))
                    item.Label = update.Message;
                item.ProgressText = update.TotalBytes > 0
                    ? FileSizeFormatter.FormatProgress(0, update.TotalBytes)
                    : update.Message ?? "";
                if (!string.IsNullOrWhiteSpace(update.Message))
                    AppendLog(update.Message);
                break;

            case TransferProgressUpdateKind.Progress:
                item.ActionType = update.ActionType;
                item.Status = TransferActionStatus.InProgress;
                item.Progress = update.Progress;
                if (!string.IsNullOrWhiteSpace(update.Message))
                    item.ProgressText = update.Message;
                else if (update.TotalBytes > 0)
                    item.ProgressText = FileSizeFormatter.FormatProgress(update.BytesTransferred, update.TotalBytes);
                break;

            case TransferProgressUpdateKind.Complete:
                item.MarkCompleted(DateTime.Now, update.Message);
                MoveFinishedItemToHistoryTop(item);
                if (!string.IsNullOrWhiteSpace(update.Message))
                    AppendLog(update.Message);
                break;

            case TransferProgressUpdateKind.Skipped:
                item.MarkSkipped(DateTime.Now, update.Message);
                MoveFinishedItemToHistoryTop(item);
                if (!string.IsNullOrWhiteSpace(update.Message))
                    AppendLog(update.Message);
                break;

            case TransferProgressUpdateKind.Failed:
                item.MarkFailed(DateTime.Now, update.Message);
                MoveFinishedItemToHistoryTop(item);
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
            // Status updated in CancelAutoClose(keepOpen: true)
        }
        finally
        {
            if (IsAutoCloseCountdownActive)
                IsAutoCloseCountdownActive = false;
        }
    }

    private void CancelAutoClose(bool keepOpen = false)
    {
        if (_autoCloseCts == null)
            return;

        _autoCloseCts.Cancel();
        _autoCloseCts = null;
        IsAutoCloseCountdownActive = false;

        if (keepOpen && !_isClosing)
            StatusText = "Ready";
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
