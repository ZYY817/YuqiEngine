using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public record UpdateInfo
{
    public string Version { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string OfficialReleaseUrl { get; init; } = string.Empty;
    public DateTime ReleaseDate { get; init; }
    public bool IsPreRelease { get; init; }
    public long Size { get; init; }
}

public sealed class UpdateCheckService : IUpdateCheckService, IDisposable
{
    private static readonly string DefaultGitHubRepo = new AppSettings().GitHubRepo;
    private readonly HttpClient _httpClient;
    private readonly string _currentVersion;
    private bool _disposed;

    public UpdateCheckService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "YuqiEngine-UpdateChecker");
        _currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";
    }

    public string CurrentVersion => _currentVersion;

    public async Task<OperationResult<UpdateInfo?>> CheckForUpdateAsync(
        bool includePreRelease = false,
        CancellationToken cancellationToken = default)
    {
        return await CheckForUpdateAsync(null, includePreRelease, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult<UpdateInfo?>> CheckForUpdateAsync(
        string? repository,
        bool includePreRelease = false,
        CancellationToken cancellationToken = default)
    {
        var loc = LocalizationService.Instance;
        var targetRepository = ResolveRepository(repository);

        if (targetRepository == null)
        {
            return OperationResult<UpdateInfo?>.Fail(loc.Get("Update.RepoNotConfigured"));
        }
        
        try
        {
            var url = includePreRelease
                ? $"https://api.github.com/repos/{targetRepository}/releases"
                : $"https://api.github.com/repos/{targetRepository}/releases/latest";
            
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return OperationResult<UpdateInfo?>.Success(null, loc.Get("Update.NoRelease"));
                }
                return OperationResult<UpdateInfo?>.Fail(loc.Get("Update.CheckFailed", response.StatusCode));
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            GitHubRelease? release = null;
            var serializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            if (includePreRelease)
            {
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, serializerOptions);
                release = releases?.FirstOrDefault(item => !item.Draft);
            }
            else
            {
                release = JsonSerializer.Deserialize<GitHubRelease>(json, serializerOptions);
            }

            if (release == null)
            {
                return OperationResult<UpdateInfo?>.Success(null, loc.Get("Update.ParseFailed"));
            }

            // 跳过预发布版本（除非明确要求）
            if (release.Prerelease && !includePreRelease)
            {
                return OperationResult<UpdateInfo?>.Success(null, loc.Get("Update.AlreadyLatestStable"));
            }

            var latestVersion = release.TagName?.TrimStart('v', 'V') ?? "0.0.0";
            
            if (!IsNewerVersion(latestVersion, _currentVersion))
            {
                return OperationResult<UpdateInfo?>.Success(null, loc.Get("Update.AlreadyLatest"));
            }

            // 查找下载链接
            var downloadUrl = release.Assets?.FirstOrDefault(a => 
                a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true ||
                a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)?.BrowserDownloadUrl 
                ?? release.HtmlUrl ?? string.Empty;

            var updateInfo = new UpdateInfo
            {
                Version = latestVersion,
                ReleaseNotes = release.Body ?? string.Empty,
                DownloadUrl = downloadUrl,
                OfficialReleaseUrl = release.HtmlUrl ?? string.Empty,
                ReleaseDate = release.PublishedAt,
                IsPreRelease = release.Prerelease,
                Size = release.Assets?.FirstOrDefault()?.Size ?? 0
            };

            return OperationResult<UpdateInfo?>.Success(updateInfo, loc.Get("Update.NewVersionFound", latestVersion));
        }
        catch (HttpRequestException ex)
        {
            return OperationResult<UpdateInfo?>.Fail(loc.Get("Update.NetworkFailed"), ex);
        }
        catch (TaskCanceledException)
        {
            return OperationResult<UpdateInfo?>.Fail(loc.Get("Update.Timeout"));
        }
        catch (Exception ex)
        {
            return OperationResult<UpdateInfo?>.Fail(loc.Get("Update.Error"), ex);
        }
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        try
        {
            if (!TryParseVersion(latest, out var latestVersion) || !TryParseVersion(current, out var currentVersion))
            {
                return false;
            }

            return latestVersion > currentVersion;
        }
        catch
        {
            return false;
        }
    }

    public async Task<OperationResult<string>> DownloadUpdatePackageAsync(
        string downloadUrl,
        string version,
        CancellationToken cancellationToken = default)
    {
        var loc = LocalizationService.Instance;

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri) ||
            (downloadUri.Scheme != Uri.UriSchemeHttp && downloadUri.Scheme != Uri.UriSchemeHttps))
        {
            return OperationResult<string>.Fail(loc.Get("Update.InvalidDownloadUrl"));
        }

        try
        {
            var updatesDirectory = Path.Combine(AppPaths.AppDataRoot, "Updates");
            Directory.CreateDirectory(updatesDirectory);

            using var response = await _httpClient
                .GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return OperationResult<string>.Fail(
                    loc.Get("Update.DownloadFailedStatus", (int)response.StatusCode, response.ReasonPhrase ?? string.Empty));
            }

            if (IsLikelyHtmlResponse(response, downloadUri))
            {
                return OperationResult<string>.Fail(loc.Get("Update.DownloadNoDirectAsset"));
            }

            var extension = ResolvePackageExtension(downloadUri.AbsolutePath);
            var safeVersion = SanitizeFileToken(version);
            var fileName = $"YuqiEngine_v{safeVersion}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
            var targetPath = Path.Combine(updatesDirectory, fileName);

            await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
            }

            return OperationResult<string>.Success(targetPath, loc.Get("Update.DownloadSaved", targetPath));
        }
        catch (HttpRequestException ex)
        {
            return OperationResult<string>.Fail(loc.Get("Update.NetworkFailed"), ex);
        }
        catch (TaskCanceledException)
        {
            return OperationResult<string>.Fail(loc.Get("Update.DownloadTimeout"));
        }
        catch (IOException ex)
        {
            return OperationResult<string>.Fail(loc.Get("Update.DownloadIoFailed", ex.Message), ex);
        }
        catch (Exception ex)
        {
            return OperationResult<string>.Fail(loc.Get("Update.Error"), ex);
        }
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0);

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().TrimStart('v', 'V');
        var metadataIndex = normalized.IndexOfAny(new[] { '-', '+' });
        if (metadataIndex > 0)
        {
            normalized = normalized[..metadataIndex];
        }

        if (!Version.TryParse(normalized, out Version? parsedVersion) || parsedVersion is null)
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }

    private static string? ResolveRepository(string? input)
    {
        var candidate = string.IsNullOrWhiteSpace(input) ? DefaultGitHubRepo : input.Trim();
        return IsValidRepository(candidate) ? candidate : null;
    }

    private static bool IsValidRepository(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var slashIndex = value.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= value.Length - 1)
        {
            return false;
        }

        if (value.IndexOf('/', slashIndex + 1) >= 0)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (ch == '/')
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsLikelyHtmlResponse(HttpResponseMessage response, Uri downloadUri)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return downloadUri.AbsolutePath.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePackageExtension(string absolutePath)
    {
        var extension = Path.GetExtension(absolutePath);
        if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase))
        {
            return extension;
        }

        return ".bin";
    }

    private static string SanitizeFileToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "unknown";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var source = token.Trim();
        var chars = new char[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            chars[i] = Array.IndexOf(invalidChars, source[i]) >= 0 ? '_' : source[i];
        }

        var value = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }

    private class GitHubRelease
    {
        public string? TagName { get; set; }
        public string? Name { get; set; }
        public string? Body { get; set; }
        public string? HtmlUrl { get; set; }
        public bool Prerelease { get; set; }
        public bool Draft { get; set; }
        public DateTime PublishedAt { get; set; }
        public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        public string? Name { get; set; }
        public string? BrowserDownloadUrl { get; set; }
        public long Size { get; set; }
    }
}
