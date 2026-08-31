namespace OBS.RecordingsTransfer.Models;

public enum TransferActionType
{
    Move,
    Delete,
    Skip,
    WaitingRemux
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
    public string? SourcePath { get; init; }
    public TransferActionType ActionType { get; init; }
    public string Description { get; init; } = "";
    public RemuxReadiness RemuxReadiness { get; init; } = RemuxReadiness.Ready;
}

public class TransferProgressUpdate
{
    public TransferProgressUpdateKind Kind { get; init; }
    public string FileName { get; init; } = "";
    public TransferActionType ActionType { get; init; }
    public double Progress { get; init; }
    public long BytesTransferred { get; init; }
    public long TotalBytes { get; init; }
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
