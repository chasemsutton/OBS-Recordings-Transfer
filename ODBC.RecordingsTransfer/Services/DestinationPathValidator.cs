namespace ODBC.RecordingsTransfer.Services;

public static class DestinationPathValidator
{
    public static bool ShouldWarnAboutYear(string destinationPath, int currentYear)
    {
        var trimmed = destinationPath.TrimEnd('\\', '/');
        if (trimmed.Length < 4)
            return false;

        var suffix = trimmed[^4..];
        if (!suffix.All(char.IsDigit))
            return false;

        if (!int.TryParse(suffix, out var folderYear))
            return false;

        return folderYear != currentYear;
    }
}
