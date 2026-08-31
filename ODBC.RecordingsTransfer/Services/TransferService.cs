using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ODBC.RecordingsTransfer.Models;

namespace ODBC.RecordingsTransfer.Services;

public class TransferService
{
    private const string Mp4Extension = ".mp4";
    private const string MkvExtension = ".mkv";
    private const int CopyBufferSize = 81920;

    private readonly LoggingService _logger;

    public TransferService(LoggingService logger)
    {
        _logger = logger;
    }

    public List<TransferActionPlan> BuildPlan(AppSettings settings)
    {
        var plan = new List<TransferActionPlan>();

        if (string.IsNullOrWhiteSpace(settings.SourcePath) || string.IsNullOrWhiteSpace(settings.DestinationPath))
            return plan;

        try
        {
            if (!Directory.Exists(settings.SourcePath) || !Directory.Exists(settings.DestinationPath))
                return plan;

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

            foreach (var mp4 in mp4Files)
                AddMp4PlanItem(mp4, settings, targetNames, plan);

            foreach (var mkv in mkvFiles)
                AddMkvPlanItem(mkv, settings, sourceNames, targetNames, dataToDelete, plan);
        }
        catch
        {
            // Blank, missing, inaccessible, or invalid paths should leave the queue empty, not crash.
        }

        return plan;
    }

    public TransferResult Run(
        AppSettings settings,
        TransferContext? context = null,
        IProgress<TransferProgressUpdate>? progress = null)
    {
        var result = new TransferResult();

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

        try
        {
            if (!Directory.Exists(settings.SourcePath) || !Directory.Exists(settings.DestinationPath))
            {
                var error = "Source or destination path does not exist.";
                result.Errors.Add(error);
                Log(error);
                return result;
            }

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

            foreach (var mp4 in mp4Files)
                ProcessMp4(mp4, settings, targetNames, result, context, Report);

            foreach (var mkv in mkvFiles)
                ProcessMkv(mkv, settings, sourceNames, targetNames, dataToDelete, result, Report);

            result.Success = result.Errors.Count == 0;
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            ErrorLogService.Write(ex);
            Log($"ERROR: {ex.Message}");
        }

        return result;
    }

    private static void AddMp4PlanItem(
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
                ActionType = TransferActionType.Skip,
                Description = $"Skip (already at destination): {fileName}"
            });
            return;
        }

        plan.Add(new TransferActionPlan
        {
            FileName = fileName,
            ActionType = TransferActionType.Move,
            Description = $"Move: {fileName}"
        });
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
                }));

                if (settings.VerifyTransfer)
                {
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

    private static void CopyFileWithProgress(string source, string destination, Action<long, long> reportBytes)
    {
        var length = new FileInfo(source).Length;
        long copied = 0;

        using var input = File.OpenRead(source);
        using var output = File.Create(destination);
        var buffer = new byte[CopyBufferSize];

        reportBytes(0, length);

        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            copied += read;
            reportBytes(copied, length);
        }
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
