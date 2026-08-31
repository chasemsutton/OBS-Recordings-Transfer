namespace ODBC.RecordingsTransfer.Models;

public class UpdateInfo
{
    public Version Version { get; init; } = new(0, 0);
    public string TagName { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string FileName { get; init; } = "";
    public UpdateChannel Channel { get; init; } = UpdateChannel.Stable;
    public bool IsCurrent { get; init; }
    public bool IsNewer { get; init; }
    public bool IsOlder { get; init; }

    /// <summary>
    /// Minimum installed version that may use in-app update to reach this release.
    /// Older installs should uninstall and reinstall from GitHub. Null = no restriction.
    /// </summary>
    public Version? MinUpdateFrom { get; init; }

    public bool RequiresManualReinstall(Version currentVersion) =>
        MinUpdateFrom != null && currentVersion < MinUpdateFrom;

    public string DisplayLabel
    {
        get
        {
            var label = $"v{Version}";
            if (IsCurrent)
                label += " (current)";
            if (Channel == UpdateChannel.Beta)
                label += " (beta)";
            return label;
        }
    }
}
