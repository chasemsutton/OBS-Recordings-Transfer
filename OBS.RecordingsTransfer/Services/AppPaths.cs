using System.IO;

namespace OBS.RecordingsTransfer.Services;

public static class AppPaths
{
    private const string AppFolderName = "OBS Recordings Transfer";
    private const string LegacyAppFolderName = "ODBC Recordings Transfer";

    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    private static string LegacyAppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LegacyAppFolderName);

    public static string ConfigFile => Path.Combine(AppDataDirectory, "config.txt");
    public static string LogFile => Path.Combine(AppDataDirectory, "logfile.txt");
    public static string ErrorFile => Path.Combine(AppDataDirectory, "programError.txt");
    public static string FfmpegErrorLog => Path.Combine(AppDataDirectory, "error.log");
    public static string IncompleteTransferFile => Path.Combine(AppDataDirectory, "incomplete-transfer.txt");

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

        var legacyAppDataConfig = Path.Combine(LegacyAppDataDirectory, "config.txt");
        if (File.Exists(legacyAppDataConfig))
        {
            try
            {
                File.Copy(legacyAppDataConfig, ConfigFile);
                return;
            }
            catch
            {
                // Fall through to other legacy locations.
            }
        }

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
