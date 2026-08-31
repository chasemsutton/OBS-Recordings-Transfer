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
        settings.CheckRemuxComplete = bool.Parse(ReadValue(lines, "Check Remux Complete", settings.CheckRemuxComplete.ToString()));
        settings.TransferMode = ParseTransferMode(lines, settings.TransferMode);
        settings.AutoRunDelaySeconds = ParseDelay(ReadValue(lines, "Auto Start Delay (seconds)", settings.AutoRunDelaySeconds.ToString()));
        settings.StartWithWindows = bool.Parse(ReadValue(lines, "Start With Windows", settings.StartWithWindows.ToString()));
        settings.StartMinimized = bool.Parse(ReadValue(lines, "Start Minimized", settings.StartMinimized.ToString()));
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
            $"Check Remux Complete: \"{settings.CheckRemuxComplete}\"",
            $"Transfer Mode: \"{settings.TransferMode}\"",
            $"Begin Transfer On Startup: \"{settings.TransferMode == TransferMode.AutoStart}\"",
            $"Auto Start Delay (seconds): \"{settings.AutoRunDelaySeconds}\"",
            $"Start With Windows: \"{settings.StartWithWindows}\"",
            $"Start Minimized: \"{settings.StartMinimized}\"",
            $"Check For Updates On Startup: \"{settings.CheckForUpdatesOnStartup}\"",
            $"Update Channel: \"{settings.UpdateChannel}\"",
            $"Skip Destination Year Warning: \"{settings.SkipDestinationYearWarning}\"",
            $"Show Settings Panel: \"{settings.ShowSettingsPanel}\""
        };

        File.WriteAllLines(_configPath, lines);
    }

    public string ConfigFilePath => _configPath;

    private static TransferMode ParseTransferMode(List<string> lines, TransferMode defaultValue)
    {
        var raw = ReadValue(lines, "Transfer Mode", "");
        if (!string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse<TransferMode>(raw, true, out var mode))
            return mode;

        // Legacy key from pre-2.3 builds.
        if (bool.TryParse(ReadValue(lines, "Begin Transfer On Startup", "False"), out var autoStart) && autoStart)
            return TransferMode.AutoStart;

        return defaultValue;
    }

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
