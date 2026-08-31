namespace ODBC.RecordingsTransfer.Models;

public class AppSettings
{
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public int MaxFileAgeDays { get; set; } = 90;
    public double MinFreeSpaceGb { get; set; } = 25;
    public int AutoCloseSeconds { get; set; } = 15;
    public bool VerifyTransfer { get; set; } = false;
    public bool VerifyRemux { get; set; } = false;
    /// <summary>When true, wait for OBS-style remux (stable size + moov) before moving MP4s.</summary>
    public bool CheckRemuxComplete { get; set; } = true;
    public bool AutoRunOnStartup { get; set; } = false;
    public int AutoRunDelaySeconds { get; set; } = 5;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;
    public bool SkipDestinationYearWarning { get; set; } = false;
    public bool ShowSettingsPanel { get; set; } = true;
}
