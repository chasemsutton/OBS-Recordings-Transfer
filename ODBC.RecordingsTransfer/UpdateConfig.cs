namespace ODBC.RecordingsTransfer;

public static class UpdateConfig
{
    public const string GitHubOwner = "chasemsutton";
    public const string GitHubRepo = "ODBC-Recordings-Transfer";
    public const string InstallerAssetSuffix = "Setup.exe";
    public const string InstallerAssetPrefix = "ODBC-Recordings-Transfer-";
    public static string ReleasesUrl => $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases";
}
