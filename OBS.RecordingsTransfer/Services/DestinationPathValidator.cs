namespace OBS.RecordingsTransfer.Services;

public static class DestinationPathValidator
{
    public static bool ShouldWarnAboutYear(string destinationPath, int currentYear)
    {
        if (!TryGetFolderYear(destinationPath, out var folderYear))
            return false;

        return folderYear != currentYear;
    }

    public static bool TryGetFolderYear(string destinationPath, out int folderYear)
    {
        folderYear = 0;
        var trimmed = destinationPath.TrimEnd('\\', '/');
        if (trimmed.Length < 4)
            return false;

        var suffix = trimmed[^4..];
        return suffix.All(char.IsDigit) && int.TryParse(suffix, out folderYear);
    }

    public static string UpdateYearInPath(string destinationPath, int year)
    {
        var trailing = "";
        var path = destinationPath;

        while (path.Length > 0 && (path[^1] == '\\' || path[^1] == '/'))
        {
            trailing = path[^1] + trailing;
            path = path[..^1];
        }

        if (path.Length < 4 || !path[^4..].All(char.IsDigit))
            return destinationPath;

        return path[..^4] + year + trailing;
    }
}
