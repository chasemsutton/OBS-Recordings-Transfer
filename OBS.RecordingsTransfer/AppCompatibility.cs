namespace OBS.RecordingsTransfer;

/// <summary>
/// Downgrade safety for the beta version picker.
/// Bump <see cref="MinCompatibleVersion"/> to the current release whenever you ship a breaking
/// config, data, or behavior change that older builds cannot handle safely.
/// </summary>
public static class AppCompatibility
{
    public static readonly Version MinCompatibleVersion = new(2, 2, 0);
}
