using ODBC.RecordingsTransfer.Models;

namespace ODBC.RecordingsTransfer.ViewModels;

public class TransferActionViewModel : ViewModelBase
{
    private TransferActionStatus _status = TransferActionStatus.Pending;
    private double _progress;
    private string _progressText = "";
    private string _label;
    private DateTime? _completedAt;

    public TransferActionViewModel(TransferActionPlan plan)
    {
        FileName = plan.FileName;
        ActionType = plan.ActionType;
        _label = plan.Description;
    }

    public string FileName { get; }
    public TransferActionType ActionType { get; }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set => SetProperty(ref _completedAt, value);
    }

    /// <summary>Finished items kept in the queue under any new pending work.</summary>
    public bool IsHistoryItem =>
        Status is TransferActionStatus.Complete
            or TransferActionStatus.Failed
            or TransferActionStatus.Skipped;

    public TransferActionStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    public void MarkCompleted(DateTime completedAt, string? message = null)
    {
        Status = TransferActionStatus.Complete;
        Progress = 1;
        ProgressText = "";
        CompletedAt = completedAt;
        Label = AppendTimestamp(message ?? DefaultCompletedLabel(), completedAt);
    }

    public void MarkSkipped(DateTime completedAt, string? message = null)
    {
        Status = TransferActionStatus.Skipped;
        ProgressText = "";
        CompletedAt = completedAt;
        if (!string.IsNullOrWhiteSpace(message))
            Label = AppendTimestamp(message, completedAt);
    }

    public void MarkFailed(DateTime completedAt, string? message = null)
    {
        Status = TransferActionStatus.Failed;
        ProgressText = "";
        CompletedAt = completedAt;
        if (!string.IsNullOrWhiteSpace(message))
            Label = AppendTimestamp(message, completedAt);
        else
            Label = AppendTimestamp($"Failed: {FileName}", completedAt);
    }

    private string DefaultCompletedLabel() => ActionType switch
    {
        TransferActionType.Delete => $"Deleted: {FileName}",
        TransferActionType.Skip => $"Skipped: {FileName}",
        _ => $"Moved: {FileName}"
    };

    private static string AppendTimestamp(string text, DateTime completedAt)
    {
        var stamp = completedAt.ToString("h:mm:ss tt");
        return $"{text}  ({stamp})";
    }
}
