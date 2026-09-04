using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OBS.RecordingsTransfer.Installer;

internal static class Program
{
    private const string AppName = "OBS Recordings Transfer";
    private const string ExeName = "OBS Recordings Transfer.exe";
    private const string LegacyAppName = "ODBC Recordings Transfer";
    private const string LegacyExeName = "ODBC Recordings Transfer.exe";
    private const string Version = "2.3.10";
    private const string Publisher = "chasemsutton";
    private const string UninstallKeyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8F4E2A91-6C3D-4B7E-9F1A-2D5E8C0B4A73}";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("-uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            var installDir = GetInstallDirectory() ?? GetDefaultInstallDirectory();
            return Uninstall(installDir);
        }

        if (args.Any(a => a.Equals("/update", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("-update", StringComparison.OrdinalIgnoreCase)))
        {
            var existingDir = GetInstallDirectory();
            if (existingDir != null)
                return Update(existingDir);
        }

        return Install(GetDefaultInstallDirectory());
    }

    private static string GetDefaultInstallDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

    private static string? GetInstallDirectory()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallKeyName);
        var location = key?.GetValue("InstallLocation") as string;
        if (string.IsNullOrWhiteSpace(location) || !Directory.Exists(location))
            return null;

        return location.TrimEnd('\\');
    }

    private static int Install(string installDir)
    {
        if (Directory.Exists(installDir) &&
            MessageBox.Show(
                $"{AppName} appears to be already installed.\n\nReinstall to the same location?",
                AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return 0;
        }

        try
        {
            var legacyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                LegacyAppName);
            if (Directory.Exists(legacyDir)
                && !string.Equals(legacyDir, installDir, StringComparison.OrdinalIgnoreCase))
            {
                StopRunningApp();
                TryDeleteDirectory(legacyDir);
                RemoveShortcut(LegacyAppName);
            }

            DeployPayload(installDir);
            RemoveLegacyFiles(installDir);

            var exePath = Path.Combine(installDir, ExeName);
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName + ".lnk"),
                exePath);

            if (MessageBox.Show("Create a desktop shortcut?", AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppName + ".lnk"),
                    exePath);
            }

            WriteDefaultConfig(installDir);
            RegisterUninstall(installDir);

            MessageBox.Show(
                $"{AppName} was installed successfully.\n\n" +
                $"Open it from the Start Menu to get started.\n\n" +
                $"Location:\n{installDir}",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Installation failed:\n\n{ex.Message}",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int Update(string installDir)
    {
        try
        {
            StopRunningApp();

            var targetDir = GetDefaultInstallDirectory();
            var migrating = !string.Equals(installDir, targetDir, StringComparison.OrdinalIgnoreCase);

            DeployPayload(targetDir);
            RemoveLegacyFiles(targetDir);

            if (migrating)
                TryDeleteDirectory(installDir);

            RegisterUninstall(targetDir);

            var exePath = Path.Combine(targetDir, ExeName);
            RemoveShortcut(LegacyAppName);
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName + ".lnk"),
                exePath);

            var legacyDesktop = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                LegacyAppName + ".lnk");
            if (File.Exists(legacyDesktop))
            {
                File.Delete(legacyDesktop);
                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppName + ".lnk"),
                    exePath);
            }

            MigrateStartupEntry();

            MessageBox.Show(
                "Update complete.\n\nYou can launch OBS Recordings Transfer now.",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Update failed:\n\n{ex.Message}",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void DeployPayload(string installDir)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OBS-Recordings-Setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            ExtractPayload(tempDir);
            CopyPayload(tempDir, installDir);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
    }

    private static void StopRunningApp()
    {
        foreach (var processName in new[]
                 {
                     Path.GetFileNameWithoutExtension(ExeName),
                     Path.GetFileNameWithoutExtension(LegacyExeName)
                 })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private static void RemoveLegacyFiles(string installDir)
    {
        TryDeleteFile(Path.Combine(installDir, LegacyExeName));
        TryDeleteFile(Path.Combine(installDir, Path.ChangeExtension(LegacyExeName, ".pdb")));
    }

    private static void MigrateStartupEntry()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (runKey == null)
                return;

            var legacy = runKey.GetValue(LegacyAppName) as string;
            if (string.IsNullOrWhiteSpace(legacy))
                return;

            runKey.SetValue(AppName, legacy.Replace(LegacyExeName, ExeName, StringComparison.OrdinalIgnoreCase)
                .Replace(LegacyAppName, AppName, StringComparison.OrdinalIgnoreCase));
            runKey.DeleteValue(LegacyAppName, throwOnMissingValue: false);
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static int Uninstall(string installDir)
    {
        if (!Directory.Exists(installDir))
        {
            MessageBox.Show("This program does not appear to be installed.", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        if (MessageBox.Show(
                $"Remove {AppName} from this computer?",
                AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return 0;
        }

        try
        {
            StopRunningApp();
            TryDeleteDirectory(installDir);

            var legacyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                LegacyAppName);
            if (Directory.Exists(legacyDir)
                && !string.Equals(legacyDir, installDir, StringComparison.OrdinalIgnoreCase))
                TryDeleteDirectory(legacyDir);

            RemoveShortcut(AppName);
            RemoveShortcut(LegacyAppName);

            using var key = Registry.LocalMachine.OpenSubKey(UninstallKeyName, writable: true);
            key?.Close();
            Registry.LocalMachine.DeleteSubKey(UninstallKeyName, throwOnMissingSubKey: false);

            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                runKey?.DeleteValue(AppName, throwOnMissingValue: false);
                runKey?.DeleteValue(LegacyAppName, throwOnMissingValue: false);
            }
            catch
            {
                // Best-effort cleanup of the optional startup entry.
            }

            MessageBox.Show($"{AppName} was removed.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Uninstall failed:\n\n{ex.Message}", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void ExtractPayload(string destination)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("app.zip", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new InvalidOperationException("Installer payload is missing. Rebuild using build-installer.bat.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var zipPath = Path.Combine(Path.GetTempPath(), "obs-payload-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var file = File.Create(zipPath))
                stream.CopyTo(file);

            ZipFile.ExtractToDirectory(zipPath, destination, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }

    private static void CopyPayload(string sourceDir, string installDir)
    {
        Directory.CreateDirectory(installDir);

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(installDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Could not create Start Menu shortcut.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
        shortcut.Description = AppName;
        shortcut.Save();

        if (Marshal.IsComObject(shell))
            Marshal.ReleaseComObject(shell);
    }

    private static void RemoveShortcut(string shortcutAppName)
    {
        TryDeleteFile(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            shortcutAppName + ".lnk"));
        TryDeleteFile(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            shortcutAppName + ".lnk"));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Best effort — leftover files are cleaned on next uninstall if needed.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort only.
        }
    }

    private static void WriteDefaultConfig(string installDir)
    {
        var configPath = Path.Combine(installDir, "config.txt");
        if (File.Exists(configPath))
            return;

        File.WriteAllText(configPath,
            "Source Path: \"\"\r\n" +
            "Destination Path: \"\"\r\n" +
            "Max File Age (days): \"90\"\r\n" +
            "Minimum Free Space (GB): \"25\"\r\n" +
            "Auto Close Timer (seconds): \"15\"\r\n" +
            "Verify Transfer: \"False\"\r\n" +
            "Verify Remux: \"False\"\r\n" +
            "Check Remux Complete: \"True\"\r\n" +
            "Assume No Direct MP4 Recording: \"False\"\r\n" +
            "Transfer Mode: \"None\"\r\n" +
            "Begin Transfer On Startup: \"False\"\r\n" +
            "Auto Start Delay (seconds): \"5\"\r\n" +
            "Start With Windows: \"False\"\r\n" +
            "Start Minimized: \"False\"\r\n" +
            "Check For Updates On Startup: \"True\"\r\n" +
            "Update Channel: \"Stable\"\r\n" +
            "Skip Destination Year Warning: \"False\"\r\n" +
            "Show Settings Panel: \"True\"\r\n");
    }

    private static void RegisterUninstall(string installDir)
    {
        var setupExe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? AppContext.BaseDirectory;

        using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyName, writable: true);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", Publisher);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", Path.Combine(installDir, ExeName));
        key.SetValue("UninstallString", $"\"{setupExe}\" /uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
}
