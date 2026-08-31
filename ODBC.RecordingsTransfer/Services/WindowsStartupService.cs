using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace ODBC.RecordingsTransfer.Services;

public static class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "ODBC Recordings Transfer";

    public static void Apply(bool startWithWindows)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key == null)
                return;

            if (!startWithWindows)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                return;
            }

            var exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath))
                return;

            key.SetValue(RunValueName, $"\"{exePath}\"");
        }
        catch
        {
            // Registry access can fail under restricted accounts; settings still persist in config.
        }
    }

    public static void Remove()
    {
        Apply(false);
    }
}
