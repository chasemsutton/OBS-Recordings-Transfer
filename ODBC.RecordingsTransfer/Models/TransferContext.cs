namespace ODBC.RecordingsTransfer.Models;

public class TransferContext
{
    public Func<string, bool>? ConfirmRetry { get; init; }
    public Action<string>? NotifyRemuxIncomplete { get; init; }
}