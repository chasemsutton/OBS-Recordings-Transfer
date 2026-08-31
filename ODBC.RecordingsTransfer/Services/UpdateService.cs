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

    public async Task<UpdateCheckResult> CheckForUpdateAsync(
        UpdateChannel channel = UpdateChannel.Stable,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var release = channel == UpdateChannel.Stable
                ? await GetLatestStableReleaseAsync(cancellationToken)
                : await GetLatestBetaReleaseAsync(cancellationToken);

            if (release == null)
                return UpdateCheckResult.Failed($"No {channel.ToString().ToLower()} release was found on GitHub.");

            var tagName = release.Value.TagName;
            var version = ParseVersion(tagName);
            if (version <= CurrentVersion)
                return UpdateCheckResult.UpToDate();

            if (!TryGetInstallerAsset(release.Value.Root, out var downloadUrl, out var fileName))
            {
                return UpdateCheckResult.Failed(
                    $"Version {version} is available, but no installer file was found in the GitHub release. " +
                    "Expected a file starting with \"ODBC-Recordings-Transfer-Setup\".");
            }

            var notes = release.Value.Root.TryGetProperty("body", out var body)
                ? body.GetString() ?? ""
                : "";

            return UpdateCheckResult.Found(new UpdateInfo
            {
                Version = version,
                TagName = tagName,
                ReleaseNotes = notes.Trim(),
                DownloadUrl = downloadUrl,
                FileName = fileName,
                Channel = channel
            });
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed(ex.Message);
        }
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
        Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
    }

    private static async Task<(JsonElement Root, string TagName)?> GetLatestStableReleaseAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{UpdateConfig.GitHubOwner}/{UpdateConfig.GitHubRepo}/releases/latest";
        using var response = await Http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement.Clone();
        var tagName = root.GetProperty("tag_name").GetString() ?? "";
        return (root, tagName);
    }

    private static async Task<(JsonElement Root, string TagName)?> GetLatestBetaReleaseAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{UpdateConfig.GitHubOwner}/{UpdateConfig.GitHubRepo}/releases?per_page=20";
        using var response = await Http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        foreach (var release in document.RootElement.EnumerateArray())
        {
            var isPrerelease = release.TryGetProperty("prerelease", out var prerelease)
                && prerelease.GetBoolean();
            if (!isPrerelease)
                continue;

            var tagName = release.GetProperty("tag_name").GetString() ?? "";
            return (release.Clone(), tagName);
        }

        return null;
    }

    private static bool TryGetInstallerAsset(JsonElement release, out string downloadUrl, out string fileName)
    {
        downloadUrl = "";
        fileName = "";

        if (!release.TryGetProperty("assets", out var assets))
            return false;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!IsInstallerAsset(name))
                continue;

            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
            fileName = name;
            return !string.IsNullOrEmpty(downloadUrl);
        }

        return false;
    }

    private static bool IsInstallerAsset(string name)
    {
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;

        if (name.StartsWith(UpdateConfig.InstallerAssetPrefix, StringComparison.OrdinalIgnoreCase)
            && name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            return true;

        return name.Contains("Recordings Transfer Setup", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
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
