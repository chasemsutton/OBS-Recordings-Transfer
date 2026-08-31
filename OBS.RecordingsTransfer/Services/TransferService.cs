using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using OBS.RecordingsTransfer.Models;

namespace OBS.RecordingsTransfer.Services;

public class TransferService
{
    private const string Mp4Extension = ".mp4";
    private const string MkvExtension = ".mkv";
    private const int CopyBufferSize = 81920;
    private static readonly TimeSpan RemuxPollInterval = TimeSpan.FromSeconds(1);

    private readonly LoggingService _logger;
    private readonly RemuxFileTracker _remuxTracker = new();
    private bool _lowSpaceNoDeletesAlertShown;
    private bool _pendingLowSpaceNoDeletesAlert;
    private double _pendingLowSpaceAvailableGb;
    private double _pendingLowSpaceMinGb;

    public TransferService(LoggingService logger)
    {
        _logger = logger;
    }

    public RemuxFileTracker RemuxTracker => _remuxTracker;

    /// <summary>
    /// Returns true once when source free space is below the minimum and no MKV deletes are planned.
    /// Only fires once per app session.
    /// </summary>
    public bool TryConsumeLowSpaceNoDeletesAlert(out double availableGb, out double minGb)
    {
        availableGb = _pendingLowSpaceAvailableGb;
        minGb = _pendingLowSpaceMinGb;
        if (!_pendingLowSpaceNoDeletesAlert)
            return false;

        _pendingLowSpaceNoDeletesAlert = false;
        return true;
    }

    public List<TransferActionPlan> BuildPlan(AppSettings settings)
    {
        var plan = new List<TransferActionPlan>();

        if (string.IsNullOrWhiteSpace(settings.SourcePath) || string.IsNullOrWhiteSpace(settings.DestinationPath))
        {
            _remuxTracker.InvalidateCache();
            return plan;
        }

        try
        {
            if (!Directory.Exists(settings.SourcePath) || !Directory.Exists(settings.DestinationPath))
            {
                _remuxTracker.InvalidateCache();
                return plan;
            }

            var sourceFiles = Directory.GetFiles(settings.SourcePath);
            var targetFiles = Directory.GetFiles(settings.DestinationPath);
            var fingerprint = RemuxFileTracker.BuildFingerprint(sourceFiles, targetFiles);

            if (_remuxTracker.TryGetCachedPlan(fingerprint, out var cached) && cached != null)
            {
                var refreshed = RefreshWaitingRemuxItems(cached, settings);
                _remuxTracker.StoreCachedPlan(fingerprint, refreshed);
                ConsiderLowSpaceNoDeletesAlert(settings, refreshed);
                return refreshed;
            }

            var sourceNames = sourceFiles.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)!;
            var targetNames = targetFiles.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)!;

            var mp4Files = sourceFiles
                .Where(f => string.Equals(Path.GetExtension(f), Mp4Extension, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var mkvFiles = sourceFiles
                .Where(f => string.Equals(Path.GetExtension(f), MkvExtension, StringComparison.OrdinalIgnoreCase))
                .ToList();

            _remuxTracker.PruneMissing(mp4Files);

            var dataToDelete = CalculateSpaceNeeded(settings, sourceFiles);

            foreach (var mp4 in mp4Files)
                AddMp4PlanItem(mp4, settings, targetNames, plan);

            foreach (var mkv in mkvFiles)
                AddMkvPlanItem(mkv, settings, sourceNames, targetNames, dataToDelete, plan);

            _remuxTracker.StoreCachedPlan(fingerprint, plan);
            ConsiderLowSpaceNoDeletesAlert(settings, plan);
        }
        catch
        {
            _remuxTracker.InvalidateCache();
            // Blank, missing, inaccessible, or invalid paths should leave the queue empty, not crash.
        }

        return plan;
    }

    private void ConsiderLowSpaceNoDeletesAlert(AppSettings settings, List<TransferActionPlan> plan)
    {
        if (_lowSpaceNoDeletesAlertShown)
            return;

        if (!TryGetAvailableGb(settings.SourcePath, out var availableGb))
            return;

        if (availableGb >= settings.MinFreeSpaceGb)
            return;

        if (plan.Any(p => p.ActionType == TransferActionType.Delete))
            return;

        _lowSpaceNoDeletesAlertShown = true;
        _pendingLowSpaceNoDeletesAlert = true;
        _pendingLowSpaceAvailableGb = availableGb;
        _pendingLowSpaceMinGb = settings.MinFreeSpaceGb;
    }

    private static bool TryGetAvailableGb(string path, out double availableGb)
    {
        availableGb = 0;
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root))
                return false;

            availableGb = new DriveInfo(root).AvailableFreeSpace / 1_073_741_824.0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private List<TransferActionPlan> RefreshWaitingRemuxItems(List<TransferActionPlan> cached, AppSettings settings)
    {
        if (!cached.Any(p => p.ActionType == TransferActionType.WaitingRemux || p.RemuxReadiness == RemuxReadiness.Waiting))
            return cached;

        var changed = false;
        var updated = new List<TransferActionPlan>(cached.Count);

        foreach (var item in cached)
        {
            if (item.ActionType is not (TransferActionType.WaitingRemux or TransferActionType.Move)
                || string.IsNullOrEmpty(item.SourcePath)
                || !settings.CheckRemuxComplete)
            {
                updated.Add(item);
                continue;
            }

            if (!File.Exists(item.SourcePath))
            {
                changed = true;
                continue;
            }

            var readiness = _remuxTracker.Evaluate(
                item.SourcePath,
                settings.CheckRemuxComplete,
                settings.AssumeNoDirectMp4Recording);
            var next = CreateMp4PlanItem(item.SourcePath, item.FileName, readiness);
            if (next.ActionType != item.ActionType
                || next.RemuxReadiness != item.RemuxReadiness
                || !string.Equals(next.Description, item.Description, StringComparison.Ordinal))
                changed = true;
            updated.Add(next);
        }

        return changed ? updated : cached;
    }

    public TransferResult Run(
        AppSettings settings,
        TransferContext? context = null,
        IProgress<TransferProgressUpdate>? progress = null)
    {
        var result = new TransferResult();
        var token = context?.CancellationToken ?? CancellationToken.None;

        void Report(TransferProgressUpdate update)
        {
            progress?.Report(update);
            if (update.Kind == TransferProgressUpdateKind.Log && !string.IsNullOrWhiteSpace(update.Message))
                _logger.Write(update.Message);
        }

        void Log(string message) => Report(new TransferProgressUpdate
        {
            Kind = TransferProgressUpdateKind.Log,
            Message = message
        });

        void ThrowIfCanceled()
        {
            if (token.IsCancellationRequested)
                throw new OperationCanceledException(token);
        }

        try
        {
            ThrowIfCanceled();

            if (!Directory.Exists(settings.SourcePath) || !Directory.Exists(settings.DestinationPath))
            {
                var error = "Source or destination path does not exist.";
                result.Errors.Add(error);
                Log(error);
                return result;
            }

            _remuxTracker.InvalidateCache();
            var plan = BuildPlan(settings);
            foreach (var item in plan)
                result.Detected.Add(item.FileName);

            var sourceFiles = Directory.GetFiles(settings.SourcePath);
            var targetFiles = Directory.GetFiles(settings.DestinationPath);
            var sourceNames = sourceFiles.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)!;
            var targetNames = targetFiles.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)!;

            var mp4Files = sourceFiles
                .Where(f => string.Equals(Path.GetExtension(f), Mp4Extension, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var mkvFiles = sourceFiles
                .Where(f => string.Equals(Path.GetExtension(f), MkvExtension, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var dataToDelete = CalculateSpaceNeeded(settings, sourceFiles);

            var readyMp4s = new List<string>();
            var waitingMp4s = new List<string>();

            foreach (var mp4 in mp4Files)
            {
                ThrowIfCanceled();
                var fileName = Path.GetFileName(mp4)!;
                if (targetNames.Contains(fileName))
                {
                    ProcessMp4(mp4, settings, targetNames, result, context, Report);
                    continue;
                }

                var readiness = settings.CheckRemuxComplete
                    ? _remuxTracker.Evaluate(mp4, true, settings.AssumeNoDirectMp4Recording)
                    : RemuxReadiness.Ready;

                switch (readiness)
                {
                    case RemuxReadiness.Ready:
                        readyMp4s.Add(mp4);
                        break;
                    case RemuxReadiness.Waiting:
                    case RemuxReadiness.Incomplete:
                        // Incomplete is a warning state (stable size, no moov yet). Keep polling —
                        // in-progress copies into the source folder can look "stable" mid-write.
                        waitingMp4s.Add(mp4);
                        if (readiness == RemuxReadiness.Incomplete)
                            NotifyIncomplete(context, fileName);
                        break;
                }
            }

            foreach (var mp4 in readyMp4s)
            {
                ThrowIfCanceled();
                ProcessMp4(mp4, settings, targetNames, result, context, Report);
            }

            foreach (var mp4 in waitingMp4s)
            {
                ThrowIfCanceled();
                var fileName = Path.GetFileName(mp4)!;
                if (!WaitForRemuxReady(mp4, settings, context, Report))
                {
                    result.Left.Add(fileName);
                    continue;
                }

                ThrowIfCanceled();
                ProcessMp4(mp4, settings, targetNames, result, context, Report);
            }

            foreach (var mkv in mkvFiles)
            {
                ThrowIfCanceled();
                ProcessMkv(mkv, settings, sourceNames, targetNames, dataToDelete, result, Report);
            }

            result.Success = result.Errors.Count == 0;
            _remuxTracker.InvalidateCache();
        }
        catch (OperationCanceledException)
        {
            Log("Transfer cancelled.");
            result.Success = false;
            result.Errors.Add("Transfer cancelled.");
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            ErrorLogService.Write(ex);
            Log($"ERROR: {ex.Message}");
        }

        return result;
    }

    private bool WaitForRemuxReady(
        string sourceFile,
        AppSettings settings,
        TransferContext? context,
        Action<TransferProgressUpdate> report)
    {
        var fileName = Path.GetFileName(sourceFile)!;
        report(new TransferProgressUpdate
        {
            Kind = TransferProgressUpdateKind.Start,
            FileName = fileName,
            ActionType = TransferActionType.WaitingRemux,
            Progress = 0,
            Message = $"Waiting for successful remux: {fileName}"
        });

        while (true)
        {
            context?.CancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(sourceFile))
            {
                report(new TransferProgressUpdate
                {
                    Kind = TransferProgressUpdateKind.Failed,
                    FileName = fileName,
                    ActionType = TransferActionType.WaitingRemux,
                    Message = $"File disappeared while waiting: {fileName}"
                });
                return false;
            }

            var readiness = _remuxTracker.Evaluate(
                sourceFile,
                settings.CheckRemuxComplete,
                settings.AssumeNoDirectMp4Recording);
            if (readiness == RemuxReadiness.Ready)
            {
                report(new TransferProgressUpdate
                {
                    Kind = TransferProgressUpdateKind.Log,
                    FileName = fileName,
                    Message = $"Remux ready: {fileName}"
                });
                return true;
            }

            if (readiness == RemuxReadiness.Incomplete)
                NotifyIncomplete(context, fileName);

            // Stay on "waiting" — never mark remux delays as skipped; move on to other ready files first.
            report(new TransferProgressUpdate
            {
                Kind = TransferProgressUpdateKind.Progress,
                FileName = fileName,
                ActionType = TransferActionType.WaitingRemux,
                Progress = 0,
                Message = "Waiting for successful remux..."
            });

            if (context?.CancellationToken.WaitHandle.WaitOne(RemuxPollInterval) == true)
                context.CancellationToken.ThrowIfCancellationRequested();
            else if (context == null)
                Thread.Sleep(RemuxPollInterval);
        }
    }

    private void NotifyIncomplete(TransferContext? context, string fileName)
    {
        if (!_remuxTracker.TryMarkIncompleteAlert(fileName))
            return;
        context?.NotifyRemuxIncomplete?.Invoke(fileName);
    }

    private void AddMp4PlanItem(
        string sourceFile,
        AppSettings settings,
        HashSet<string?> targetNames,
        List<TransferActionPlan> plan)
    {
        var fileName = Path.GetFileName(sourceFile)!;

        if (targetNames.Contains(fileName))
        {
            plan.Add(new TransferActionPlan
            {
                FileName = fileName,
                SourcePath = sourceFile,
                ActionType = TransferActionType.Skip,
                Description = $"Skip (already at destination): {fileName}"
            });
            return;
        }

        var readiness = _remuxTracker.Evaluate(
            sourceFile,
            settings.CheckRemuxComplete,
            settings.AssumeNoDirectMp4Recording);
        plan.Add(CreateMp4PlanItem(sourceFile, fileName, readiness));
    }

    private static TransferActionPlan CreateMp4PlanItem(string sourceFile, string fileName, RemuxReadiness readiness)
    {
        return readiness switch
        {
            RemuxReadiness.Waiting => new TransferActionPlan
            {
                FileName = fileName,
                SourcePath = sourceFile,
                ActionType = TransferActionType.WaitingRemux,
                RemuxReadiness = RemuxReadiness.Waiting,
                Description = $"Waiting for successful remux: {fileName}"
            },
            RemuxReadiness.Incomplete => new TransferActionPlan
            {
                FileName = fileName,
                SourcePath = sourceFile,
                ActionType = TransferActionType.WaitingRemux,
                RemuxReadiness = RemuxReadiness.Incomplete,
                Description = $"Waiting for successful remux: {fileName}"
            },
            _ => new TransferActionPlan
            {
                FileName = fileName,
                SourcePath = sourceFile,
                ActionType = TransferActionType.Move,
                RemuxReadiness = RemuxReadiness.Ready,
                Description = $"Move: {fileName}"
            }
        };
    }

    private static void AddMkvPlanItem(
        string sourceFile,
        AppSettings settings,
        HashSet<string?> sourceNames,
        HashSet<string?> targetNames,
        double dataToDelete,
        List<TransferActionPlan> plan)
    {
        var fileName = Path.GetFileName(sourceFile)!;
        var mp4Name = Path.GetFileNameWithoutExtension(fileName) + Mp4Extension;

        DateTime created;
        try
        {
            created = File.GetCreationTime(sourceFile);
        }
        catch
        {
            return;
        }

        var fileAge = DateTime.Now - created;

        var shouldDelete = fileAge.TotalDays > settings.MaxFileAgeDays
            || (dataToDelete > 0 && fileAge.TotalDays > 8);

        if (!shouldDelete)
            return;

        var mp4Exists = sourceNames.Contains(mp4Name) || targetNames.Contains(mp4Name);
        if (!mp4Exists)
            return;

        plan.Add(new TransferActionPlan
        {
            FileName = fileName,
            SourcePath = sourceFile,
            ActionType = TransferActionType.Delete,
            Description = $"Delete old MKV: {fileName}"
        });
    }

    private static double CalculateSpaceNeeded(AppSettings settings, string[] sourceFiles)
    {
        try
        {
            if (sourceFiles.Length == 0)
                return 0;

            var drive = new DriveInfo(Path.GetPathRoot(sourceFiles[0])!);
            var remainingGb = drive.AvailableFreeSpace / 1_073_741_824.0;
            return remainingGb < settings.MinFreeSpaceGb ? settings.MinFreeSpaceGb - remainingGb : 0;
        }
        catch
        {
            return 0;
        }
    }

    private void ProcessMp4(
        string sourceFile,
        AppSettings settings,
        HashSet<string?> targetNames,
        TransferResult result,
        TransferContext? context,
        Action<TransferProgressUpdate> report)
    {
        var fileName = Path.GetFileName(sourceFile)!;

        if (targetNames.Contains(fileName))
        {
            result.Left.Add(fileName);
            report(new TransferProgressUpdate
            {
                Kind = TransferProgressUpdateKind.Skipped,
                FileName = fileName,
                ActionType = TransferActionType.Skip,
                Message = $"Skipped (already at destination): {fileName}"
            });
            return;
        }

        var fileSize = new FileInfo(sourceFile).Length;

        report(new TransferProgressUpdate
        {
            Kind = TransferProgressUpdateKind.Start,
            FileName = fileName,
            ActionType = TransferActionType.Move,
            Progress = 0,
            TotalBytes = fileSize,
            Message = $"Copying: {fileName}"
        });

        if (settings.VerifyRemux)
        {
            report(new TransferProgressUpdate
            {
                Kind = TransferProgressUpdateKind.Progress,
                FileName = fileName,
                ActionType = TransferActionType.Move,
                Progress = 0,
                TotalBytes = fileSize,
                BytesTransferred = 0,
                Message = "Validating remux..."
            });
        }

        if (settings.VerifyRemux && !IsRemuxComplete(sourceFile, message => report(new TransferProgressUpdate
        {
            Kind = TransferProgressUpdateKind.Log,
            FileName = fileName,
            Message = message
        })))
        {
            result.Left.Add(fileName);
            report(new TransferProgressUpdate
            {
                Kind = TransferProgressUpdateKind.Skipped,
                FileName = fileName,
                ActionType = TransferActionType.Skip,
                Message = $"Remux validation failed, leaving: {fileName}"
            });
            return;
        }

        var destFile = Path.Combine(settings.DestinationPath, fileName);
        var retry = true;

        while (retry)
        {
            try
            {
                if (File.Exists(destFile))
                    File.Delete(destFile);

                CopyFileWithProgress(sourceFile, destFile, (copied, total) => report(new TransferProgressUpdate
                {
                    Kind = TransferProgressUpdateKind.Progress,
                    FileName = fileName,
                    ActionType = TransferActionType.Move,
                    Progress = total > 0 ? copied / (double)total : 1,
                    BytesTransferred = copied,
                    TotalBytes = total
                }), context?.CancellationToken ?? CancellationToken.None);

                if (settings.VerifyTransfer)
                {
                    report(new TransferProgressUpdate
                    {
                        Kind = TransferProgressUpdateKind.Progress,
                        FileName = fileName,
                        ActionType = TransferActionType.Move,
                        Progress = 1,
                        Message = "Verifying..."
                    });
                    report(new TransferProgressUpdate
                    {
                        Kind = TransferProgressUpdateKind.Log,
                        FileName = fileName,
                        Message = $"Verifying transfer: {fileName}"
                    });

                    var sourceHash = ComputeMd5(sourceFile);
                    var destHash = ComputeMd5(destFile);

                    if (!string.Equals(sourceHash, destHash, StringComparison.Ordinal))
                    {
                        report(new TransferProgressUpdate
                        {
                            Kind = TransferProgressUpdateKind.Log,
                            FileName = fileName,
                            Message = $"Transfer verification failed: {fileName}"
                        });

                        retry = context?.ConfirmRetry?.Invoke(fileName) ?? false;
                        if (retry)
                        {
                            if (File.Exists(destFile))
                                File.Delete(destFile);
                            continue;
                        }

                        result.Errors.Add($"Transfer verification failed: {fileName}");
                        report(new TransferProgressUpdate
                        {
                            Kind = TransferProgressUpdateKind.Failed,
                            FileName = fileName,
                            ActionType = TransferActionType.Move,
                            Message = $"Failed: {fileName}"
                        });
                        return;
                    }

                    report(new TransferProgressUpdate
                    {
                        Kind = TransferProgressUpdateKind.Log,
                        FileName = fileName,
                        Message = $"Transfer verified: {fileName}"
                    });
                }

                report(new TransferProgressUpdate
                {
                    Kind = TransferProgressUpdateKind.Progress,
                    FileName = fileName,
                    ActionType = TransferActionType.Move,
                    Progress = 1,
                    Message = "Removing original..."
                });
                File.Delete(sourceFile);
                result.Moved.Add(fileName);
                targetNames.Add(fileName);
                report(new TransferProgressUpdate
                {
                    Kind = TransferProgressUpdateKind.Complete,
                    FileName = fileName,
                    ActionType = TransferActionType.Move,
                    Progress = 1,
                    Message = $"Moved: {fileName}"
                });
                return;
            }
            catch (Exception ex)
            {
                report(new TransferProgressUpdate
                {
                    Kind = TransferProgressUpdateKind.Log,
                    FileName = fileName,
                    Message = $"Error transferring {fileName}: {ex.Message}"
                });

                retry = context?.ConfirmRetry?.Invoke(fileName) ?? false;
                if (!retry)
                {
                    result.Errors.Add($"{fileName}: {ex.Message}");
                    ErrorLogService.Write(ex);
                    report(new TransferProgressUpdate
                    {
                        Kind = TransferProgressUpdateKind.Failed,
                        FileName = fileName,
                        ActionType = TransferActionType.Move,
                        Message = $"Failed: {fileName}"
                    });
                    return;
                }
            }
        }
    }

    private void ProcessMkv(
        string sourceFile,
        AppSettings settings,
        HashSet<string?> sourceNames,
        HashSet<string?> targetNames,
        double dataToDelete,
        TransferResult result,
        Action<TransferProgressUpdate> report)
    {
        var fileName = Path.GetFileName(sourceFile)!;
        var mp4Name = Path.GetFileNameWithoutExtension(fileName) + Mp4Extension;
        var fileAge = DateTime.Now - File.GetCreationTime(sourceFile);

        var shouldDelete = fileAge.TotalDays > settings.MaxFileAgeDays
            || (dataToDelete > 0 && fileAge.TotalDays > 8);

        if (!shouldDelete)
        {
            result.Left.Add(fileName);
            return;
        }

        var mp4Exists = sourceNames.Contains(mp4Name) || targetNames.Contains(mp4Name);
        if (!mp4Exists)
        {
            result.Left.Add(fileName);
            return;
        }

        report(new TransferProgressUpdate
        {
            Kind = TransferProgressUpdateKind.Start,
            FileName = fileName,
            ActionType = TransferActionType.Delete,
            Progress = 0,
            Message = $"Deleting: {fileName}"
        });

        try
        {
            File.Delete(sourceFile);
            result.Deleted.Add(fileName);
            report(new TransferProgressUpdate
            {
                Kind = TransferProgressUpdateKind.Complete,
                FileName = fileName,
                ActionType = TransferActionType.Delete,
                Progress = 1,
                Message = $"Deleted old MKV: {fileName}"
            });
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{fileName}: {ex.Message}");
            report(new TransferProgressUpdate
            {
                Kind = TransferProgressUpdateKind.Failed,
                FileName = fileName,
                ActionType = TransferActionType.Delete,
                Message = $"Error deleting {fileName}: {ex.Message}"
            });
            ErrorLogService.Write(ex);
        }
    }

    private static void CopyFileWithProgress(
        string source,
        string destination,
        Action<long, long> reportBytes,
        CancellationToken cancellationToken)
    {
        var length = new FileInfo(source).Length;
        long copied = 0;
        var lastReport = Stopwatch.StartNew();

        void Report(bool force)
        {
            if (force || lastReport.ElapsedMilliseconds >= 50)
            {
                reportBytes(copied, length);
                lastReport.Restart();
            }
        }

        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.SequentialScan);
        var buffer = new byte[CopyBufferSize];

        Report(true);

        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            copied += read;
            Report(false);
        }

        output.Flush(true);
        Report(true);
    }

    private static bool IsRemuxComplete(string sourceFile, Action<string> report)
    {
        var ffmpegPath = FFmpegService.FindExecutable();
        if (ffmpegPath == null)
        {
            report("FFmpeg not found; skipping remux validation.");
            return true;
        }

        report($"Validating remux: {Path.GetFileName(sourceFile)}");

        var errorLog = AppPaths.FfmpegErrorLog;
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-v error -i \"{sourceFile}\" -f null -",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)!;
        var errorText = process.StandardError.ReadToEnd();
        process.WaitForExit();

        File.WriteAllText(errorLog, errorText);
        return string.IsNullOrWhiteSpace(errorText);
    }

    private static string ComputeMd5(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var md5 = MD5.Create();
        return Convert.ToBase64String(md5.ComputeHash(stream));
    }
}
