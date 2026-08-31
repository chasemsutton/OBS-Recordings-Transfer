using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ODBC.RecordingsTransfer.Models;

namespace ODBC.RecordingsTransfer.Services;

public class ConfigService
{
    private readonly string _configPath;

    public ConfigService(string? configPath = null)
    {
        AppPaths.MigrateLegacyConfigIfNeeded();
        _configPath = configPath ?? AppPaths.ConfigFile;
    }

    public AppSettings Load()
    {
        var settings = new AppSettings();

        if (!File.Exists(_configPath))
        {
            Save(settings);
            return settings;
        }

        var lines = File.ReadAllLines(_configPath).ToList();
        settings.SourcePath = ReadValue(lines, "Source Path", settings.SourcePath);
        settings.DestinationPath = ReadValue(lines, "Destination Path", settings.DestinationPath);
        settings.MaxFileAgeDays = int.Parse(ReadValue(lines, "Max File Age (days)", settings.MaxFileAgeDays.ToString()));
        settings.MinFreeSpaceGb = double.Parse(ReadValue(lines, "Minimum Free Space (GB)", settings.MinFreeSpaceGb.ToString()));
        settings.AutoCloseSeconds = int.Parse(ReadValue(lines, "Auto Close Timer (seconds)", settings.AutoCloseSeconds.ToString()));
        settings.VerifyTransfer = bool.Parse(ReadValue(lines, "Verify Transfer", settings.VerifyTransfer.ToString()));
        settings.VerifyRemux = bool.Parse(ReadValue(lines, "Verify Remux", settings.VerifyRemux.ToString()));
        settings.AutoRunOnStartup = bool.Parse(ReadValue(lines, "Begin Transfer On Startup", settings.AutoRunOnStartup.ToString()));
        settings.AutoRunDelaySeconds = ParseDelay(ReadValue(lines, "Auto Start Delay (seconds)", settings.AutoRunDelaySeconds.ToString()));
        settings.CheckForUpdatesOnStartup = bool.Parse(ReadValue(lines, "Check For Updates On Startup", settings.CheckForUpdatesOnStartup.ToString()));
        settings.UpdateChannel = Enum.TryParse<UpdateChannel>(
            ReadValue(lines, "Update Channel", settings.UpdateChannel.ToString()), true, out var channel)
            ? channel
            : UpdateChannel.Stable;
        settings.SkipDestinationYearWarning = bool.Parse(
            ReadValue(lines, "Skip Destination Year Warning", settings.SkipDestinationYearWarning.ToString()));
        settings.ShowSettingsPanel = bool.Parse(
            ReadValue(lines, "Show Settings Panel", settings.ShowSettingsPanel.ToString()));

        return settings;
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureAppDataDirectory();
        var lines = new[]
        {
            $"Source Path: \"{settings.SourcePath}\"",
            $"Destination Path: \"{settings.DestinationPath}\"",
            $"Max File Age (days): \"{settings.MaxFileAgeDays}\"",
            $"Minimum Free Space (GB): \"{settings.MinFreeSpaceGb}\"",
            $"Auto Close Timer (seconds): \"{settings.AutoCloseSeconds}\"",
            $"Verify Transfer: \"{settings.VerifyTransfer}\"",
            $"Verify Remux: \"{settings.VerifyRemux}\"",
            $"Begin Transfer On Startup: \"{settings.AutoRunOnStartup}\"",
            $"Auto Start Delay (seconds): \"{settings.AutoRunDelaySeconds}\"",
            $"Check For Updates On Startup: \"{settings.CheckForUpdatesOnStartup}\"",
            $"Update Channel: \"{settings.UpdateChannel}\"",
            $"Skip Destination Year Warning: \"{settings.SkipDestinationYearWarning}\"",
            $"Show Settings Panel: \"{settings.ShowSettingsPanel}\""
        };

        File.WriteAllLines(_configPath, lines);
    }

    public string ConfigFilePath => _configPath;

    private static int ParseDelay(string value)
    {
        return int.TryParse(value, out var seconds) && seconds >= 0 ? seconds : 5;
    }

    private static string ReadValue(List<string> lines, string key, string defaultValue)
    {
        var line = lines.FirstOrDefault(l => l.Contains(key, StringComparison.OrdinalIgnoreCase));
        if (line == null)
            return defaultValue;

        var parts = line.Split('"');
        return parts.Length >= 2 ? parts[1] : defaultValue;
    }
}
