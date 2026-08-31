namespace ODBC.RecordingsTransfer.Services;

public static class FileSizeFormatter
{
    private const double Gibibyte = 1_073_741_824.0;

    public static string FormatGigabytes(long bytes) => $"{bytes / Gibibyte:0.0} GB";

    public static string FormatProgress(long copied, long total) =>
        $"{FormatGigabytes(copied)} / {FormatGigabytes(total)}";
}
