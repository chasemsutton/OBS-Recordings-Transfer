using System;
using System.Collections.Generic;
using System.IO;
using ODBC.RecordingsTransfer.Models;

namespace ODBC.RecordingsTransfer.Services;

public class LoggingService
{
    private readonly string _logPath;

    public LoggingService(string? logPath = null)
    {
        _logPath = logPath ?? Path.Combine(AppContext.BaseDirectory, "logfile.txt");
    }

    public event Action<string>? LogMessage;

    public void Write(string message)
    {
        LogMessage?.Invoke(message);
    }

    public void WriteResult(TransferResult result)
    {
        using var log = File.Exists(_logPath) ? File.AppendText(_logPath) : new StreamWriter(_logPath);

        log.WriteLine($"{DateTime.Now} -------------------------------------------");

        if (result.Detected.Count == 0)
        {
            log.WriteLine("Files Detected--------------------------------------");
            log.WriteLine("None");
            log.WriteLine();
            return;
        }

        WriteSection(log, "Files Detected", result.Detected);
        WriteSection(log, "Files Moved", result.Moved);
        WriteSection(log, "Files Deleted", result.Deleted);
        WriteSection(log, "Files Left", result.Left);

        if (result.Errors.Count > 0)
            WriteSection(log, "Errors", result.Errors);
    }

    private static void WriteSection(StreamWriter log, string title, List<string> items)
    {
        log.WriteLine($"{title}-----------------------------------------");
        if (items.Count == 0)
            log.WriteLine("None");
        else
            foreach (var item in items)
                log.WriteLine(item);
        log.WriteLine();
    }
}
