namespace ODBC.RecordingsTransfer.Models;

public class UpdateInfo
{
    public Version Version { get; init; } = new(0, 0);
    public string TagName { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string FileName { get; init; } = "";
    public UpdateChannel Channel { get; init; } = UpdateChannel.Stable;
}
