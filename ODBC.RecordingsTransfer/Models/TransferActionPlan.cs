namespace ODBC.RecordingsTransfer.Models;

public enum TransferActionType
{
    Move,
    Delete,
    Skip
}

public enum TransferActionStatus
{
    Pending,
    InProgress,
    Complete,
    Skipped,
    Failed
}

public class TransferActionPlan
{
    public string FileName { get; init; } = "";
    public TransferActionType ActionType { get; init; }
    public string Description { get; init; } = "";
}

public class TransferProgressUpdate
{
    public TransferProgressUpdateKind Kind { get; init; }
    public string FileName { get; init; } = "";
    public TransferActionType ActionType { get; init; }
    public double Progress { get; init; }
    public string? Message { get; init; }
}

public enum TransferProgressUpdateKind
{
    Plan,
    Start,
    Progress,
    Complete,
    Skipped,
    Failed,
    Log
}
