namespace ODBC.RecordingsTransfer.Models;

public class UpdateCheckResult
{
    public bool UpdateAvailable { get; init; }
    public UpdateInfo? Update { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateCheckResult UpToDate() => new();

    public static UpdateCheckResult Found(UpdateInfo update) => new()
    {
        UpdateAvailable = true,
        Update = update
    };

    public static UpdateCheckResult Failed(string message) => new()
    {
        ErrorMessage = message
    };
}
