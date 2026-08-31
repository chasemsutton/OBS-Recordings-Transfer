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
        _configPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "config.txt");
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
        settings.CheckForUpdatesOnStartup = bool.Parse(ReadValue(lines, "Check For Updates On Startup", settings.CheckForUpdatesOnStartup.ToString()));

        return settings;
    }

    public void Save(AppSettings settings)
    {
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
            $"Check For Updates On Startup: \"{settings.CheckForUpdatesOnStartup}\""
        };

        File.WriteAllLines(_configPath, lines);
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
