using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KsitalTelemetryHub.UI.WinUI.Services;

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = new();
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public static class UpdateService
{
    private const string RepoOwner = "ntvampire";
    private const string RepoName = "TelemetryHub";
    private static readonly HttpClient HttpClient = new();

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TelemetryHub-Updater", "1.0"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        HttpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public static string GetCurrentVersionString()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "2.0.3";
    }

    public static async Task<(bool HasUpdate, string CurrentVersion, string RemoteVersion, string? ReleaseNotes, string? DownloadUrl)> CheckForUpdatesAsync()
    {
        string currentVerStr = GetCurrentVersionString();
        string apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        try
        {
            var response = await HttpClient.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode)
            {
                return (false, currentVerStr, currentVerStr, null, null);
            }

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return (false, currentVerStr, currentVerStr, null, null);
            }

            string rawTag = release.TagName.TrimStart('v', 'V');
            if (Version.TryParse(rawTag, out var remoteVer) && Version.TryParse(currentVerStr, out var localVer))
            {
                if (remoteVer > localVer)
                {
                    // Поиск инсталлятора среди ассетов релиза
                    var installerAsset = release.Assets.Find(a => a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                        ?? release.Assets.Find(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                    return (true, currentVerStr, release.TagName, release.Body, installerAsset?.BrowserDownloadUrl);
                }
            }

            return (false, currentVerStr, release.TagName, release.Body, null);
        }
        catch (Exception ex)
        {
            App.LogError("UpdateService.CheckForUpdatesAsync", ex);
            return (false, currentVerStr, currentVerStr, null, null);
        }
    }

    public static async Task<string> DownloadInstallerAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"telemetry-hub-setup-{DateTime.Now.Ticks}.exe");

        using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
            totalRead += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                int percentage = (int)((totalRead * 100) / totalBytes.Value);
                progress?.Report(percentage);
            }
        }

        return tempFile;
    }

    public static void LaunchInstallerAndExit(string installerPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/CLOSEAPPLICATIONS",
                UseShellExecute = true
            };
            Process.Start(psi);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            App.LogError("UpdateService.LaunchInstallerAndExit", ex);
        }
    }
}
