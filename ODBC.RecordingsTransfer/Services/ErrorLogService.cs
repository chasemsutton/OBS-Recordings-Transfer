using System.IO;

namespace ODBC.RecordingsTransfer.Services;

public static class ErrorLogService
{
    private static readonly string ErrorPath = Path.Combine(AppContext.BaseDirectory, "programError.txt");

    public static void Write(Exception ex)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} -------------------------------------------{Environment.NewLine}{ex}{Environment.NewLine}";
            File.AppendAllText(ErrorPath, line);
        }
        catch
        {
            // Best effort only.
        }
    }

    public static string ErrorFilePath => ErrorPath;
}
