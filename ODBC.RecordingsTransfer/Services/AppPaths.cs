using System.IO;

namespace ODBC.RecordingsTransfer.Services;

public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ODBC Recordings Transfer");

    public static string ConfigFile => Path.Combine(AppDataDirectory, "config.txt");
    public static string LogFile => Path.Combine(AppDataDirectory, "logfile.txt");
    public static string ErrorFile => Path.Combine(AppDataDirectory, "programError.txt");
    public static string FfmpegErrorLog => Path.Combine(AppDataDirectory, "error.log");

    private static string LegacyConfigFile => Path.Combine(AppContext.BaseDirectory, "config.txt");

    public static void EnsureAppDataDirectory()
    {
        Directory.CreateDirectory(AppDataDirectory);
    }

    public static void MigrateLegacyConfigIfNeeded()
    {
        EnsureAppDataDirectory();

        if (File.Exists(ConfigFile))
            return;

        if (!File.Exists(LegacyConfigFile))
            return;

        try
        {
            File.Copy(LegacyConfigFile, ConfigFile);
        }
        catch
        {
            // If migration fails, a fresh config will be created on first save.
        }
    }
}
