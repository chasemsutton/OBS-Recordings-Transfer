using System.IO;
using OBS.RecordingsTransfer.Models;

namespace OBS.RecordingsTransfer.Services;

public static class IncompleteTransferStore
{
    private static string StateFilePath => AppPaths.IncompleteTransferFile;

    public static void Save(string sourceFilePath, string destinationFolder, string fileName, long sourceSize)
    {
        try
        {
            AppPaths.EnsureAppDataDirectory();
            var lines = new[]
            {
                $"Source Path: \"{sourceFilePath}\"",
                $"Destination Path: \"{destinationFolder}\"",
                $"File Name: \"{fileName}\"",
                $"Source Size: {sourceSize}"
            };
            File.WriteAllLines(StateFilePath, lines);
        }
        catch
        {
            // Best effort only — transfer can still proceed.
        }
    }

    public static IncompleteTransferState? TryLoad()
    {
        try
        {
            if (!File.Exists(StateFilePath))
                return null;

            var lines = File.ReadAllLines(StateFilePath).ToList();
            var sourcePath = ReadValue(lines, "Source Path");
            var destinationPath = ReadValue(lines, "Destination Path");
            var fileName = ReadValue(lines, "File Name");
            var sourceSizeText = ReadValue(lines, "Source Size");

            if (string.IsNullOrWhiteSpace(sourcePath)
                || string.IsNullOrWhiteSpace(destinationPath)
                || string.IsNullOrWhiteSpace(fileName)
                || !long.TryParse(sourceSizeText, out var sourceSize)
                || sourceSize < 0)
                return null;

            return new IncompleteTransferState
            {
                SourcePath = sourcePath,
                DestinationPath = destinationPath,
                FileName = fileName,
                SourceSize = sourceSize
            };
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(StateFilePath))
                File.Delete(StateFilePath);
        }
        catch
        {
            // Best effort only.
        }
    }

    private static string ReadValue(IReadOnlyList<string> lines, string key)
    {
        var line = lines.FirstOrDefault(l => l.Contains(key, StringComparison.OrdinalIgnoreCase));
        if (line == null)
            return "";

        var parts = line.Split('"');
        return parts.Length >= 2 ? parts[1] : "";
    }
}
