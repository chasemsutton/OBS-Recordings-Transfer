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
    public bool AutoRunOnStartup { get; set; } = false;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
}
