using System.IO;

namespace OBS.RecordingsTransfer.Services;

public static class ErrorLogService
{
    public static void Write(Exception ex)
    {
        try
        {
            AppPaths.EnsureAppDataDirectory();
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} -------------------------------------------{Environment.NewLine}{ex}{Environment.NewLine}";
            File.AppendAllText(AppPaths.ErrorFile, line);
        }
        catch
        {
            // Best effort only.
        }
    }

    public static string ErrorFilePath => AppPaths.ErrorFile;
}
