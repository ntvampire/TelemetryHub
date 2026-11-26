using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.UI.Avalonia.Services;

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

public record UpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    string RemoteVersion,
    string? ReleaseNotes,
    string? DownloadUrl,
    string? AssetName,
    string? ChecksumsUrl,
    string? ReleasePageUrl);

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
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "2.3.0";
    }

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        string currentVerStr = GetCurrentVersionString();
        string apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        try
        {
            var response = await HttpClient.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, currentVerStr, currentVerStr, null, null, null, null, null);
            }

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateCheckResult(false, currentVerStr, currentVerStr, null, null, null, null, null);
            }

            string rawTag = release.TagName.TrimStart('v', 'V');
            if (Version.TryParse(rawTag, out var remoteVer) && Version.TryParse(currentVerStr, out var localVer) && remoteVer > localVer)
            {
                GitHubAsset? targetAsset;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (!Environment.Is64BitOperatingSystem)
                    {
                        targetAsset = release.Assets.Find(a => a.Name.Contains("x86", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                      ?? release.Assets.Find(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        targetAsset = release.Assets.Find(a => a.Name.Contains("win7-11-x64", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                      ?? release.Assets.Find(a => a.Name.Contains("win7-11", StringComparison.OrdinalIgnoreCase) && !a.Name.Contains("x86", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                      ?? release.Assets.Find(a => a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase) && !a.Name.Contains("x86", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                      ?? release.Assets.Find(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    targetAsset = release.Assets.Find(a => a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));
                }

                var checksumAsset = release.Assets.Find(a =>
                    a.Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase) ||
                    a.Name.Contains("sha256", StringComparison.OrdinalIgnoreCase));

                return new UpdateCheckResult(
                    true,
                    currentVerStr,
                    release.TagName,
                    release.Body,
                    targetAsset?.BrowserDownloadUrl,
                    targetAsset?.Name,
                    checksumAsset?.BrowserDownloadUrl,
                    release.HtmlUrl);
            }

            return new UpdateCheckResult(false, currentVerStr, release.TagName, release.Body, null, null, null, release.HtmlUrl);
        }
        catch (Exception ex)
        {
            App.LogError("UpdateService.CheckForUpdatesAsync", ex);
            return new UpdateCheckResult(false, currentVerStr, currentVerStr, null, null, null, null, null);
        }
    }

    public static async Task<string> DownloadInstallerAsync(
        string downloadUrl,
        string? assetName = null,
        string? checksumsUrl = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Разрешена загрузка обновлений только по HTTPS.");

        string updatesDir = Path.Combine(AppPaths.GetDataDirectory(), "updates");
        Directory.CreateDirectory(updatesDir);

        string safeFileName = !string.IsNullOrWhiteSpace(assetName)
            ? Path.GetFileName(assetName)
            : $"telemetry-hub-setup-{Guid.NewGuid():N}.exe";
        string tempFile = Path.Combine(updatesDir, safeFileName);

        using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
        using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
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
        }

        // Проверяем целостность скачанного файла
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && tempFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            byte[] mz = new byte[2];
            using (var fs = File.OpenRead(tempFile))
            {
                if (fs.Read(mz, 0, 2) < 2 || mz[0] != 0x4D || mz[1] != 0x5A)
                {
                    File.Delete(tempFile);
                    throw new InvalidDataException("Скачанный файл не является корректным исполняемым файлом Windows.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(checksumsUrl) && !string.IsNullOrWhiteSpace(assetName))
        {
            await VerifySha256FromChecksumsAsync(tempFile, assetName, checksumsUrl, ct);
        }

        return tempFile;
    }

    private static async Task VerifySha256FromChecksumsAsync(string filePath, string assetName, string checksumsUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(checksumsUrl, UriKind.Absolute, out var csUri) || csUri.Scheme != Uri.UriSchemeHttps)
            return;

        string content = await HttpClient.GetStringAsync(csUri, ct);
        string? expectedHash = null;

        foreach (var rawLine in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = rawLine.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                string filePart = Path.GetFileName(parts[1].TrimStart('*'));
                if (string.Equals(filePart, assetName, StringComparison.OrdinalIgnoreCase))
                {
                    expectedHash = parts[0].Trim();
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(expectedHash))
            return;

        using var sha = SHA256.Create();
        await using var fs = File.OpenRead(filePath);
        byte[] actualBytes = await sha.ComputeHashAsync(fs, ct);
        string actualHash = Convert.ToHexString(actualBytes);

        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            fs.Close();
            try { File.Delete(filePath); } catch { }
            throw new InvalidDataException($"Контрольная сумма SHA-256 обновления не совпала! Ожидалось: {expectedHash}, получено: {actualHash}");
        }
    }

    public static void LaunchInstallerAndExit(string installerPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("Автоматический запуск .exe-установщика поддерживается только в Windows.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/CLOSEAPPLICATIONS",
            UseShellExecute = true
        };
        Process.Start(psi);
        Environment.Exit(0);
    }
}
