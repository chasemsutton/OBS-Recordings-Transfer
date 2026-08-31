namespace ODBC.RecordingsTransfer.Models;

public enum RemuxReadiness
{
    /// <summary>Remux check disabled or not an MP4 candidate.</summary>
    Ready,
    /// <summary>File is still growing or moov not present yet.</summary>
    Waiting,
    /// <summary>Size stable for the timeout with no moov atom; do not move.</summary>
    Incomplete
}
