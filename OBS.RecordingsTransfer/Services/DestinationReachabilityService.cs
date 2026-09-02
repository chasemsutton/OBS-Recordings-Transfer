using System.IO;
using OBS.RecordingsTransfer.Models;

namespace OBS.RecordingsTransfer.Services;

public static class DestinationReachabilityService
{
    private const int CheckTimeoutMs = 5000;
    private const string ProbeFilePrefix = ".obs-transfer-probe-";

    /// <summary>
    /// Lightweight check: folder exists and can be listed (local paths and UNC shares).
    /// </summary>
    public static DestinationReachabilityStatus CheckExists(string? destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return DestinationReachabilityStatus.NotConfigured;

        try
        {
            var exists = RunWithTimeout(
                () => Directory.Exists(destinationPath),
                CheckTimeoutMs);

            return exists
                ? DestinationReachabilityStatus.Reachable
                : DestinationReachabilityStatus.Unreachable;
        }
        catch
        {
            return DestinationReachabilityStatus.Unreachable;
        }
    }

    /// <summary>
    /// Write probe: folder exists and a temporary file can be created and deleted.
    /// </summary>
    public static DestinationReachabilityStatus CheckWriteAccess(string? destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return DestinationReachabilityStatus.NotConfigured;

        var existsStatus = CheckExists(destinationPath);
        if (existsStatus != DestinationReachabilityStatus.Reachable)
            return existsStatus;

        try
        {
            var canWrite = RunWithTimeout(
                () => TryWriteProbe(destinationPath),
                CheckTimeoutMs);

            return canWrite
                ? DestinationReachabilityStatus.Reachable
                : DestinationReachabilityStatus.NoWritePermission;
        }
        catch
        {
            return DestinationReachabilityStatus.NoWritePermission;
        }
    }

    private static bool TryWriteProbe(string destinationPath)
    {
        var probePath = Path.Combine(destinationPath, ProbeFilePrefix + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            return true;
        }
        catch
        {
            TryDeleteProbe(probePath);
            return false;
        }
    }

    private static void TryDeleteProbe(string probePath)
    {
        try
        {
            if (File.Exists(probePath))
                File.Delete(probePath);
        }
        catch
        {
            // Best effort only.
        }
    }

    private static T RunWithTimeout<T>(Func<T> action, int timeoutMs)
    {
        var task = Task.Run(action);
        if (!task.Wait(timeoutMs))
            throw new TimeoutException("Destination check timed out.");

        return task.Result;
    }
}
