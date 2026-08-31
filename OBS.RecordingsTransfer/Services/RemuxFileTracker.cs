using System.IO;
using System.Linq;
using System.Text;
using OBS.RecordingsTransfer.Models;

namespace OBS.RecordingsTransfer.Services;

/// <summary>
/// Session-only tracker for MP4 remux readiness (size stability + moov). Not persisted.
/// </summary>
public class RemuxFileTracker
{
    public static readonly TimeSpan IncompleteAfterStable = TimeSpan.FromSeconds(30);
    /// <summary>Extra dwell after moov is found when direct-to-MP4 recording may still be in progress.</summary>
    public static readonly TimeSpan PostMoovStabilityWait = TimeSpan.FromSeconds(20);

    private readonly Dictionary<string, FileWatchState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _alertedIncomplete = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastFingerprint;
    private List<TransferActionPlan>? _cachedPlan;

    public bool TryMarkIncompleteAlert(string fileName) => _alertedIncomplete.Add(fileName);

    public IReadOnlyList<string> ConsumeIncompleteAlerts()
    {
        var due = new List<string>();
        foreach (var state in _states.Values)
        {
            if (state.Readiness != RemuxReadiness.Incomplete)
                continue;
            if (!TryMarkIncompleteAlert(state.FileName))
                continue;
            due.Add(state.FileName);
        }

        return due;
    }

    public void InvalidateCache()
    {
        _lastFingerprint = null;
        _cachedPlan = null;
    }

    public bool TryGetCachedPlan(string fingerprint, out List<TransferActionPlan>? plan)
    {
        if (_lastFingerprint != null
            && string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal)
            && _cachedPlan != null)
        {
            plan = _cachedPlan;
            return true;
        }

        plan = null;
        return false;
    }

    public void StoreCachedPlan(string fingerprint, List<TransferActionPlan> plan)
    {
        _lastFingerprint = fingerprint;
        _cachedPlan = plan;
    }

    public static string BuildFingerprint(string[] sourceFiles, string[] targetFiles)
    {
        var sb = new StringBuilder(sourceFiles.Length * 48 + targetFiles.Length * 24);
        foreach (var path in sourceFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            long size = 0;
            try { size = new FileInfo(path).Length; } catch { /* ignore */ }
            sb.Append(Path.GetFileName(path)).Append('|').Append(size).Append(';');
        }

        sb.Append("=>");
        foreach (var name in targetFiles.Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            sb.Append(name).Append(';');

        return sb.ToString();
    }

    public RemuxReadiness Evaluate(string sourceFile, bool checkEnabled, bool assumeNoDirectMp4Recording = true)
    {
        var fileName = Path.GetFileName(sourceFile) ?? sourceFile;
        if (!checkEnabled)
        {
            _states.Remove(sourceFile);
            _alertedIncomplete.Remove(fileName);
            return RemuxReadiness.Ready;
        }

        long size;
        try
        {
            size = new FileInfo(sourceFile).Length;
        }
        catch
        {
            return RemuxReadiness.Waiting;
        }

        var now = DateTime.UtcNow;
        if (!_states.TryGetValue(sourceFile, out var state))
        {
            state = new FileWatchState { FileName = fileName };
            _states[sourceFile] = state;
        }

        if (state.LastSize >= 0 && state.LastSize != size)
        {
            // File is still growing (recording, remux, or copy into the source folder).
            state.LastSize = size;
            state.StableSinceUtc = null;
            state.HasMoov = false;
            state.MoovChecked = false;
            state.DefinitiveMissingSinceUtc = null;
            SetReadiness(state, RemuxReadiness.Waiting);
            return RemuxReadiness.Waiting;
        }

        if (state.LastSize < 0)
            state.LastSize = size;

        state.StableSinceUtc ??= now;

        var probe = Mp4MoovProbe.Probe(sourceFile);
        switch (probe)
        {
            case MoovProbeResult.Found:
                state.HasMoov = true;
                state.MoovChecked = true;
                state.DefinitiveMissingSinceUtc = null;

                // Remux-only workflows can move as soon as moov appears on a non-growing file.
                // Direct-to-MP4 recording needs a longer stable window so a brief size pause
                // mid-record cannot start a transfer.
                if (assumeNoDirectMp4Recording
                    || now - state.StableSinceUtc.Value >= PostMoovStabilityWait)
                {
                    SetReadiness(state, RemuxReadiness.Ready);
                    return RemuxReadiness.Ready;
                }

                SetReadiness(state, RemuxReadiness.Waiting);
                return RemuxReadiness.Waiting;

            case MoovProbeResult.Unavailable:
                // Locked/partial reads during copy must not count as "moov missing".
                state.HasMoov = false;
                state.MoovChecked = false;
                state.DefinitiveMissingSinceUtc = null;
                SetReadiness(state, RemuxReadiness.Waiting);
                return RemuxReadiness.Waiting;

            default:
                state.HasMoov = false;
                state.MoovChecked = true;
                state.DefinitiveMissingSinceUtc ??= now;
                break;
        }

        if (now - state.DefinitiveMissingSinceUtc.Value >= IncompleteAfterStable
            && now - state.StableSinceUtc.Value >= IncompleteAfterStable)
        {
            SetReadiness(state, RemuxReadiness.Incomplete);
            return RemuxReadiness.Incomplete;
        }

        SetReadiness(state, RemuxReadiness.Waiting);
        return RemuxReadiness.Waiting;
    }

    private void SetReadiness(FileWatchState state, RemuxReadiness readiness)
    {
        if (state.Readiness == RemuxReadiness.Incomplete
            && readiness != RemuxReadiness.Incomplete)
        {
            // Allow a fresh alert if the file looks stuck again later.
            _alertedIncomplete.Remove(state.FileName);
        }

        state.Readiness = readiness;
    }

    public void PruneMissing(IEnumerable<string> existingSourcePaths)
    {
        var keep = new HashSet<string>(existingSourcePaths, StringComparer.OrdinalIgnoreCase);
        var remove = _states.Keys.Where(k => !keep.Contains(k)).ToList();
        foreach (var key in remove)
        {
            if (_states.TryGetValue(key, out var state))
                _alertedIncomplete.Remove(state.FileName);
            _states.Remove(key);
        }
    }

    private sealed class FileWatchState
    {
        public string FileName { get; init; } = "";
        public long LastSize { get; set; } = -1;
        public DateTime? StableSinceUtc { get; set; }
        public DateTime? DefinitiveMissingSinceUtc { get; set; }
        public bool MoovChecked { get; set; }
        public bool HasMoov { get; set; }
        public RemuxReadiness Readiness { get; set; } = RemuxReadiness.Waiting;
    }
}
