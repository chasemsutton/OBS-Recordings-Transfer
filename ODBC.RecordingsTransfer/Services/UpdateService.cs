using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using ODBC.RecordingsTransfer.Models;

namespace ODBC.RecordingsTransfer.Services;

public class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    public Version CurrentVersion { get; } = GetCurrentVersion();

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{UpdateConfig.GitHubOwner}/{UpdateConfig.GitHubRepo}/releases/latest";
        using var response = await Http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tagName = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tagName);
        if (version <= CurrentVersion)
            return null;

        var downloadUrl = "";
        var fileName = "";
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(UpdateConfig.InstallerAssetSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!name.StartsWith(UpdateConfig.InstallerAssetPrefix, StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("Recordings Transfer Setup", StringComparison.OrdinalIgnoreCase))
                    continue;

                downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                fileName = name;
                break;
            }
        }

        if (string.IsNullOrEmpty(downloadUrl))
            return null;

        var notes = root.TryGetProperty("body", out var body)
            ? body.GetString() ?? ""
            : "";

        return new UpdateInfo
        {
            Version = version,
            TagName = tagName,
            ReleaseNotes = notes.Trim(),
            DownloadUrl = downloadUrl,
            FileName = fileName
        };
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "ODBC-Recordings-Update");
        Directory.CreateDirectory(folder);

        var destination = Path.Combine(folder, update.FileName);

        using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;

        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            readTotal += read;

            if (total > 0)
                progress?.Report(readTotal / (double)total);
        }

        progress?.Report(1);
        return destination;
    }

    public void LaunchInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ODBC-Recordings-Transfer", GetCurrentVersion().ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info) && Version.TryParse(info.Split('+')[0], out var parsed))
            return parsed;

        return assembly.GetName().Version ?? new Version(1, 0, 0);
    }

    private static Version ParseVersion(string tagName)
    {
        var cleaned = tagName.TrimStart('v', 'V');
        return Version.TryParse(cleaned, out var version) ? version : new Version(0, 0);
    }
}
