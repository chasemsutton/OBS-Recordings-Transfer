using System.Collections.Generic;

namespace OBS.RecordingsTransfer.Models;

public class TransferResult
{
    public List<string> Detected { get; } = new();
    public List<string> Moved { get; } = new();
    public List<string> Deleted { get; } = new();
    public List<string> Left { get; } = new();
    public List<string> Errors { get; } = new();
    public bool Success { get; set; }
}
