// File: GameOptimizer.Shell/Services/UpdateCheckService.cs
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services;

public sealed record UpdateCheckResult(
    bool CheckSucceeded,
    bool UpdateAvailable,
    string? LatestTag,
    string? LatestUrl,
    string? FailureReason);

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckLatestReleaseAsync(
        string repository,
        string currentVersion,
        CancellationToken cancellationToken);
}

/// <summary>
/// 真实的版本检测：查询 GitHub releases/latest，按语义化版本与当前程序集版本比较。
/// 网络失败、无 release、tag 无法解析都返回 CheckSucceeded=false 而非静默成功。
/// </summary>
public sealed class UpdateCheckService : IUpdateCheckService, IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public UpdateCheckService() : this(CreateClient(), ownsClient: true) { }

    public UpdateCheckService(HttpClient httpClient, bool ownsClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = ownsClient;
    }

    public async Task<UpdateCheckResult> CheckLatestReleaseAsync(
        string repository,
        string currentVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return new UpdateCheckResult(false, false, null, null, "repository.invalid");

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repository.Trim().Trim('/')}/releases/latest");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(false, false, null, null, "release.none");
            }
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, false, null, null, $"http.{(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var tag = document.RootElement.TryGetProperty("tag_name", out var tagEl)
                ? tagEl.GetString()
                : null;
            var url = document.RootElement.TryGetProperty("html_url", out var urlEl)
                ? urlEl.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(tag))
            {
                return new UpdateCheckResult(false, false, null, url, "tag.unparseable");
            }

            var latest = ParseVersion(tag);
            var current = ParseVersion(currentVersion);
            if (latest is null || current is null)
            {
                return new UpdateCheckResult(false, false, tag, url, "version.unparseable");
            }

            return new UpdateCheckResult(true, latest > current, tag, url, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(false, false, null, null, "timeout");
        }
        catch (HttpRequestException)
        {
            return new UpdateCheckResult(false, false, null, null, "network");
        }
        catch (JsonException)
        {
            return new UpdateCheckResult(false, false, null, null, "response.invalid");
        }
        catch (Exception)
        {
            return new UpdateCheckResult(false, false, null, null, "unknown");
        }
    }

    // Why: tag 形如 v0.1.5-preview+commit、当前版本 0.1.4-preview+hash；
    // 只比较数值部分，预览通道不进入大小比较（0.1.5-preview 仍 > 0.1.4）。
    internal static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var candidate = text.Trim().TrimStart('v', 'V');
        var cut = candidate.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
            candidate = candidate[..cut];
        return Version.TryParse(candidate, out var version) ? version : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(8),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YuqiEngine-UpdateCheck/0.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
