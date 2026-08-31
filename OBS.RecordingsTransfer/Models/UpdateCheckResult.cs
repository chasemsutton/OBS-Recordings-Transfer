namespace OBS.RecordingsTransfer.Models;

public class UpdateCheckResult
{
    public bool UpdateAvailable { get; init; }
    public UpdateInfo? Update { get; init; }
    public IReadOnlyList<UpdateInfo> CompatibleReleases { get; init; } = Array.Empty<UpdateInfo>();
    public string? ErrorMessage { get; init; }

    public static UpdateCheckResult UpToDate() => new();

    public static UpdateCheckResult Found(UpdateInfo update) => new()
    {
        UpdateAvailable = true,
        Update = update,
        CompatibleReleases = new[] { update }
    };

    public static UpdateCheckResult FoundBetaList(IReadOnlyList<UpdateInfo> releases, UpdateInfo? preferred)
    {
        var installable = releases.Where(r => !r.IsCurrent).ToList();
        return new UpdateCheckResult
        {
            UpdateAvailable = preferred != null && !preferred.IsCurrent,
            Update = preferred,
            CompatibleReleases = releases
        };
    }

    public static UpdateCheckResult Failed(string message) => new()
    {
        ErrorMessage = message
    };
}
