namespace OBS.RecordingsTransfer.Models;

public class IncompleteTransferState
{
    public string SourcePath { get; init; } = "";
    public string DestinationPath { get; init; } = "";
    public string FileName { get; init; } = "";
    public long SourceSize { get; init; }
}
