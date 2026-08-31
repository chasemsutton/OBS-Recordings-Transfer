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

    private readonly LoggingService _logger;

    public TransferService(LoggingService logger)
    {
        _logger = logger;
    }

    public TransferResult Run(AppSettings settings, TransferContext? context = null, IProgress<string>? progress = null)
    {
        var result = new TransferResult();

        void Report(string message)
        {
            progress?.Report(message);
            _logger.Write(message);
        }

        try
        {
            if (!Directory.Exists(settings.SourcePath) || !Directory.Exists(settings.DestinationPath))
            {
                var error = "Source or destination path does not exist.";
                result.Errors.Add(error);
                Report(error);
                return result;
            }

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

            foreach (var file in mp4Files.Concat(mkvFiles))
                result.Detected.Add(Path.GetFileName(file)!);

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
            Report($"ERROR: {ex.Message}");
        }

        return result;
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
        Action<string> report)
    {
        var fileName = Path.GetFileName(sourceFile)!;

        if (targetNames.Contains(fileName))
        {
            result.Left.Add(fileName);
            report($"Skipped (already at destination): {fileName}");
            return;
        }

        if (settings.VerifyRemux && !IsRemuxComplete(sourceFile, report))
        {
            result.Left.Add(fileName);
            report($"Remux validation failed, leaving: {fileName}");
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

                report($"Copying: {fileName}");
                File.Copy(sourceFile, destFile);

                if (settings.VerifyTransfer)
                {
                    report($"Verifying transfer: {fileName}");
                    var sourceHash = ComputeMd5(sourceFile);
                    var destHash = ComputeMd5(destFile);

                    if (!string.Equals(sourceHash, destHash, StringComparison.Ordinal))
                    {
                        report($"Transfer verification failed: {fileName}");
                        retry = context?.ConfirmRetry?.Invoke(fileName) ?? false;

                        if (retry)
                        {
                            if (File.Exists(destFile))
                                File.Delete(destFile);
                            continue;
                        }

                        result.Errors.Add($"Transfer verification failed: {fileName}");
                        return;
                    }

                    report($"Transfer verified: {fileName}");
                }

                File.Delete(sourceFile);
                result.Moved.Add(fileName);
                targetNames.Add(fileName);
                report($"Moved: {fileName}");
                return;
            }
            catch (Exception ex)
            {
                report($"Error transferring {fileName}: {ex.Message}");
                retry = context?.ConfirmRetry?.Invoke(fileName) ?? false;

                if (!retry)
                {
                    result.Errors.Add($"{fileName}: {ex.Message}");
                    ErrorLogService.Write(ex);
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
        Action<string> report)
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

        try
        {
            File.Delete(sourceFile);
            result.Deleted.Add(fileName);
            report($"Deleted old MKV: {fileName}");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{fileName}: {ex.Message}");
            report($"Error deleting {fileName}: {ex.Message}");
            ErrorLogService.Write(ex);
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

        var errorLog = Path.Combine(AppContext.BaseDirectory, "error.log");
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
