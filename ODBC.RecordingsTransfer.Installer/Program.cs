using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ODBC.RecordingsTransfer.Installer;

internal static class Program
{
    private const string AppName = "ODBC Recordings Transfer";
    private const string ExeName = "ODBC Recordings Transfer.exe";
    private const string Version = "2.3.8";
    private const string Publisher = "Open Door Baptist Church";
    private const string UninstallKeyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8F4E2A91-6C3D-4B7E-9F1A-2D5E8C0B4A73}";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("-uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            var installDir = GetInstallDirectory() ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                AppName);
            return Uninstall(installDir);
        }

        if (args.Any(a => a.Equals("/update", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("-update", StringComparison.OrdinalIgnoreCase)))
        {
            var existingDir = GetInstallDirectory();
            if (existingDir != null)
                return Update(existingDir);
        }

        var defaultInstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            AppName);
        return Install(defaultInstallDir);
    }

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
            DeployPayload(installDir);

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
            DeployPayload(installDir);
            RegisterUninstall(installDir);

            var exePath = Path.Combine(installDir, ExeName);
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName + ".lnk"),
                exePath);

            MessageBox.Show(
                "Update complete.\n\nYou can launch the program now.",
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
        var tempDir = Path.Combine(Path.GetTempPath(), "ODBC-Recordings-Setup-" + Guid.NewGuid().ToString("N"));
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
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
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
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try { process.Kill(); process.WaitForExit(5000); } catch { /* ignore */ }
            }

            Directory.Delete(installDir, true);

            var startMenuLink = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                AppName + ".lnk");
            if (File.Exists(startMenuLink))
                File.Delete(startMenuLink);

            var desktopLink = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                AppName + ".lnk");
            if (File.Exists(desktopLink))
                File.Delete(desktopLink);

            using var key = Registry.LocalMachine.OpenSubKey(UninstallKeyName, writable: true);
            key?.Close();
            Registry.LocalMachine.DeleteSubKey(UninstallKeyName, throwOnMissingSubKey: false);

            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                runKey?.DeleteValue("ODBC Recordings Transfer", throwOnMissingValue: false);
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
        var zipPath = Path.Combine(Path.GetTempPath(), "odbc-payload-" + Guid.NewGuid().ToString("N") + ".zip");
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
