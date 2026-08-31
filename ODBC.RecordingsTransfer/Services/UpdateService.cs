using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ODBC.RecordingsTransfer.Models;

namespace ODBC.RecordingsTransfer.Services;

public class UpdateService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex CompatMinRegex = new(
        @"<!--\s*compat-min:\s*([0-9]+(?:\.[0-9]+){1,3})\s*-->",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Version CurrentVersion { get; } = GetCurrentVersion();

    public async Task<UpdateCheckResult> CheckForUpdateAsync(
        UpdateChannel channel = UpdateChannel.Stable,
        bool includeOlderBetas = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (channel == UpdateChannel.Stable)
                return await CheckStableAsync(cancellationToken);

            return await CheckBetaAsync(includeOlderBetas, cancellationToken);
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
        Process.Start(new ProcessStartInfo(installerPath, "/update") { UseShellExecute = true });
    }

    private async Task<UpdateCheckResult> CheckStableAsync(CancellationToken cancellationToken)
    {
        var release = await GetLatestStableReleaseAsync(cancellationToken);
        if (release == null)
            return UpdateCheckResult.Failed("No stable release was found on GitHub.");

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

        var notes = GetReleaseNotes(release.Value.Root);
        return UpdateCheckResult.Found(new UpdateInfo
        {
            Version = version,
            TagName = tagName,
            ReleaseNotes = notes,
            DownloadUrl = downloadUrl,
            FileName = fileName,
            Channel = UpdateChannel.Stable,
            IsNewer = true
        });
    }

    private async Task<UpdateCheckResult> CheckBetaAsync(bool includeOlderBetas, CancellationToken cancellationToken)
    {
        var releases = await ListCompatibleBetaReleasesAsync(cancellationToken);
        if (releases.Count == 0)
            return UpdateCheckResult.Failed("No compatible beta releases were found on GitHub.");

        var newer = releases.FirstOrDefault(r => r.IsNewer);
        if (!includeOlderBetas)
        {
            if (newer == null)
                return UpdateCheckResult.UpToDate();

            return UpdateCheckResult.FoundBetaList(releases, newer);
        }

        // Manual check: always return the full compatible list; prefer newest newer, else current, else newest overall.
        var preferred = newer
            ?? releases.FirstOrDefault(r => r.IsCurrent)
            ?? releases[0];

        return UpdateCheckResult.FoundBetaList(releases, preferred);
    }

    public async Task<IReadOnlyList<UpdateInfo>> ListCompatibleBetaReleasesAsync(
        CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{UpdateConfig.GitHubOwner}/{UpdateConfig.GitHubRepo}/releases?per_page=30";
        using var response = await Http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return Array.Empty<UpdateInfo>();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var results = new List<UpdateInfo>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                continue;

            var isPrerelease = release.TryGetProperty("prerelease", out var prerelease)
                && prerelease.GetBoolean();
            if (!isPrerelease)
                continue;

            var tagName = release.GetProperty("tag_name").GetString() ?? "";
            var version = ParseVersion(tagName);
            if (version.Major == 0 && version.Minor == 0)
                continue;

            var notes = GetReleaseNotes(release);
            var releaseFloor = ParseCompatMin(notes) ?? AppCompatibility.MinCompatibleVersion;
            var effectiveFloor = releaseFloor > AppCompatibility.MinCompatibleVersion
                ? releaseFloor
                : AppCompatibility.MinCompatibleVersion;

            if (version < effectiveFloor)
                continue;

            if (!TryGetInstallerAsset(release, out var downloadUrl, out var fileName))
                continue;

            var cmp = version.CompareTo(CurrentVersion);
            results.Add(new UpdateInfo
            {
                Version = version,
                TagName = tagName,
                ReleaseNotes = notes,
                DownloadUrl = downloadUrl,
                FileName = fileName,
                Channel = UpdateChannel.Beta,
                IsCurrent = cmp == 0,
                IsNewer = cmp > 0,
                IsOlder = cmp < 0
            });
        }

        return results
            .OrderByDescending(r => r.Version)
            .ToList();
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

    private static string GetReleaseNotes(JsonElement release)
    {
        if (!release.TryGetProperty("body", out var body))
            return "";
        return (body.GetString() ?? "").Trim();
    }

    private static Version? ParseCompatMin(string releaseNotes)
    {
        var match = CompatMinRegex.Match(releaseNotes);
        if (!match.Success)
            return null;

        return Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
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
