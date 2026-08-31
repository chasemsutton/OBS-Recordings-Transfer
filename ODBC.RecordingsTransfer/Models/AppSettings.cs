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
    /// <summary>
    /// When true (typical OBS remux workflow), become Ready as soon as moov is found on a stable file.
    /// When false, require extra stable time after moov so a live direct-to-MP4 recording is less likely to be copied early.
    /// </summary>
    public bool AssumeNoDirectMp4Recording { get; set; } = false;
    public TransferMode TransferMode { get; set; } = TransferMode.None;
    public int AutoRunDelaySeconds { get; set; } = 5;
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;
    public bool SkipDestinationYearWarning { get; set; } = false;
    public bool ShowSettingsPanel { get; set; } = true;
}
