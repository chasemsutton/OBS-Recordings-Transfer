using ODBC.RecordingsTransfer.Models;

namespace ODBC.RecordingsTransfer.ViewModels;

public class TransferActionViewModel : ViewModelBase
{
    private TransferActionStatus _status = TransferActionStatus.Pending;
    private double _progress;

    public TransferActionViewModel(TransferActionPlan plan)
    {
        FileName = plan.FileName;
        ActionType = plan.ActionType;
        Label = plan.Description;
    }

    public string FileName { get; }
    public TransferActionType ActionType { get; }
    public string Label { get; }

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
}
