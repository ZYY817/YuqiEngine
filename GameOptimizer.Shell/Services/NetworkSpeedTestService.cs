using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GameOptimizer.Shell.Services;

public enum NetworkSpeedTestPhase
{
    Preflight,
    Latency,
    Download,
    Upload,
    Finalizing
}

public enum NetworkSpeedTestFailureKind
{
    Offline,
    PermissionDenied,
    NodeUnavailable,
    InvalidNode
}

public sealed record NetworkSpeedTestNode(
    string Id,
    string Name,
    string Description,
    Uri DownloadUri,
    Uri UploadUri,
    int DownloadBytes,
    int UploadBytes);

public sealed record NetworkSpeedTestProgress(
    NetworkSpeedTestPhase Phase,
    double Percent,
    string Message,
    double? MegabitsPerSecond = null);

public sealed record NetworkSpeedTestResult(
    NetworkSpeedTestNode Node,
    double DnsResolutionMs,
    bool NodeReachable,
    double AverageLatencyMs,
    double MinimumLatencyMs,
    double JitterMs,
    double PacketLossPercent,
    double DownloadMbps,
    double UploadMbps,
    long DownloadedBytes,
    long UploadedBytes,
    TimeSpan Duration,
    DateTimeOffset CompletedAtUtc);

public sealed class NetworkSpeedTestException : Exception
{
    public NetworkSpeedTestException(NetworkSpeedTestFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public NetworkSpeedTestFailureKind Kind { get; }
}

public interface INetworkSpeedTestService
{
    IReadOnlyList<NetworkSpeedTestNode> BuiltInNodes { get; }

    bool TryCreateCustomNode(string input, out NetworkSpeedTestNode? node, out string error);

    Task<NetworkSpeedTestResult> RunAsync(
        NetworkSpeedTestNode node,
        IProgress<NetworkSpeedTestProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class NetworkSpeedTestService : INetworkSpeedTestService, IDisposable
{
    private const int LatencySampleCount = 5;
    private static readonly TimeSpan LatencySampleTimeout = TimeSpan.FromSeconds(3);
    private readonly HttpClient _httpClient;
    private readonly Func<bool> _networkAvailable;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _dnsResolver;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public NetworkSpeedTestService()
        : this(CreateHttpClient(), NetworkInterface.GetIsNetworkAvailable, ownsHttpClient: true)
    {
    }

    internal NetworkSpeedTestService(
        HttpClient httpClient,
        Func<bool>? networkAvailable = null,
        bool ownsHttpClient = false,
        Func<string, CancellationToken, Task<IPAddress[]>>? dnsResolver = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _networkAvailable = networkAvailable ?? NetworkInterface.GetIsNetworkAvailable;
        _dnsResolver = dnsResolver ?? ((host, cancellationToken) => Dns.GetHostAddressesAsync(host, cancellationToken));
        _ownsHttpClient = ownsHttpClient;
        BuiltInNodes = CreateBuiltInNodes();
    }

    public IReadOnlyList<NetworkSpeedTestNode> BuiltInNodes { get; }

    public bool TryCreateCustomNode(string input, out NetworkSpeedTestNode? node, out string error)
    {
        node = null;
        error = string.Empty;

        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var baseUri) ||
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(baseUri.Host) ||
            !string.IsNullOrWhiteSpace(baseUri.UserInfo))
        {
            error = "请输入不含账号信息的 HTTPS 基址，例如 https://speed.example.com/。";
            return false;
        }

        var rootBuilder = new UriBuilder(baseUri)
        {
            Path = baseUri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
                ? baseUri.AbsolutePath
                : baseUri.AbsolutePath + "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        var root = rootBuilder.Uri;
        node = new NetworkSpeedTestNode(
            "custom",
            $"自定义 · {baseUri.Host}",
            "Cloudflare 兼容协议 · 约下载 8 MiB / 上传 2 MiB",
            new Uri(root, "__down"),
            new Uri(root, "__up"),
            8 * 1024 * 1024,
            2 * 1024 * 1024);
        return true;
    }

    public async Task<NetworkSpeedTestResult> RunAsync(
        NetworkSpeedTestNode node,
        IProgress<NetworkSpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(node);
        ValidateNode(node);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_networkAvailable())
        {
            throw new NetworkSpeedTestException(
                NetworkSpeedTestFailureKind.Offline,
                "Windows 未检测到可用网络连接。请连接网络后重试。 ");
        }

        var runStopwatch = Stopwatch.StartNew();
        progress?.Report(new NetworkSpeedTestProgress(
            NetworkSpeedTestPhase.Preflight,
            4,
            "正在检查所选节点与请求边界…"));

        try
        {
            var dnsResolution = await MeasureDnsResolutionAsync(node, progress, cancellationToken).ConfigureAwait(false);
            var latency = await MeasureLatencyAsync(node, progress, cancellationToken).ConfigureAwait(false);
            var download = await MeasureDownloadAsync(node, progress, cancellationToken).ConfigureAwait(false);
            var upload = await MeasureUploadAsync(node, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report(new NetworkSpeedTestProgress(
                NetworkSpeedTestPhase.Finalizing,
                96,
                "正在核对样本与生成本轮结果…"));

            runStopwatch.Stop();
            var result = new NetworkSpeedTestResult(
                node,
                dnsResolution,
                NodeReachable: true,
                latency.Average,
                latency.Minimum,
                latency.Jitter,
                latency.LossPercent,
                download.Mbps,
                upload.Mbps,
                download.Bytes,
                upload.Bytes,
                runStopwatch.Elapsed,
                DateTimeOffset.UtcNow);

            progress?.Report(new NetworkSpeedTestProgress(
                NetworkSpeedTestPhase.Finalizing,
                100,
                "测试完成；结果仅代表本轮节点与当前网络环境。"));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NetworkSpeedTestException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new NetworkSpeedTestException(
                NetworkSpeedTestFailureKind.PermissionDenied,
                "节点或网络策略拒绝了测速请求（HTTP 401/403）。",
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new NetworkSpeedTestException(
                NetworkSpeedTestFailureKind.PermissionDenied,
                "当前网络策略或权限不允许完成测速请求。",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new NetworkSpeedTestException(
                NetworkSpeedTestFailureKind.NodeUnavailable,
                "测速节点不可达，或代理、证书、防火墙策略阻止了连接。",
                ex);
        }
        catch (SocketException ex)
        {
            throw new NetworkSpeedTestException(
                NetworkSpeedTestFailureKind.NodeUnavailable,
                "无法解析测速节点，或当前 DNS / 网络策略阻止了访问。",
                ex);
        }
    }

    private async Task<double> MeasureDnsResolutionAsync(
        NetworkSpeedTestNode node,
        IProgress<NetworkSpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new NetworkSpeedTestProgress(
            NetworkSpeedTestPhase.Preflight,
            7,
            "正在解析测速节点 DNS…"));

        var stopwatch = Stopwatch.StartNew();
        var addresses = await _dnsResolver(node.DownloadUri.DnsSafeHost, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        if (addresses.Length == 0)
        {
            throw new NetworkSpeedTestException(
                NetworkSpeedTestFailureKind.NodeUnavailable,
                "测速节点未返回可用地址。请更换节点或检查 DNS 配置。");
        }

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_ownsHttpClient)
            _httpClient.Dispose();
        _disposed = true;
    }

    private async Task<(double Average, double Minimum, double Jitter, double LossPercent)> MeasureLatencyAsync(
        NetworkSpeedTestNode node,
        IProgress<NetworkSpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var samples = new List<double>(LatencySampleCount);
        var lost = 0;

        for (var index = 0; index < LatencySampleCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new NetworkSpeedTestProgress(
                NetworkSpeedTestPhase.Latency,
                8 + index * 6,
                $"正在采样 HTTP RTT（{index + 1}/{LatencySampleCount}）…"));

            using var sampleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sampleCts.CancelAfter(LatencySampleTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, AddQuery(node.DownloadUri, 0));
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    sampleCts.Token).ConfigureAwait(false);
                ThrowForFailure(response);
                stopwatch.Stop();
                samples.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lost++;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden)
            {
                lost++;
            }
        }

        if (samples.Count == 0)
        {
            throw new NetworkSpeedTestException(
                NetworkSpeedTestFailureKind.NodeUnavailable,
                "节点未返回任何有效延迟样本。请更换节点或检查代理、防火墙与证书策略。");
        }

        var jitter = samples.Count > 1
            ? samples.Zip(samples.Skip(1), (left, right) => Math.Abs(right - left)).Average()
            : 0;
        return (
            samples.Average(),
            samples.Min(),
            jitter,
            lost * 100.0 / LatencySampleCount);
    }

    private async Task<(double Mbps, long Bytes)> MeasureDownloadAsync(
        NetworkSpeedTestNode node,
        IProgress<NetworkSpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new NetworkSpeedTestProgress(
            NetworkSpeedTestPhase.Download,
            40,
            $"正在下载约 {FormatMebibytes(node.DownloadBytes)} MiB 合成数据…"));

        using var request = new HttpRequestMessage(HttpMethod.Get, AddQuery(node.DownloadUri, node.DownloadBytes));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        ThrowForFailure(response);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[64 * 1024];
        long totalBytes = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        while (true)
        {
            var remaining = node.DownloadBytes - totalBytes;
            if (remaining <= 0)
                break;

            var read = await stream.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            totalBytes += read;
            if (stopwatch.Elapsed - lastReport < TimeSpan.FromMilliseconds(150))
                continue;

            lastReport = stopwatch.Elapsed;
            var liveMbps = ToMegabitsPerSecond(totalBytes, stopwatch.Elapsed);
            var fraction = Math.Clamp(totalBytes / (double)node.DownloadBytes, 0, 1);
            progress?.Report(new NetworkSpeedTestProgress(
                NetworkSpeedTestPhase.Download,
                40 + fraction * 25,
                $"下载测试进行中 · 已接收 {FormatMebibytes(totalBytes):F1} MiB",
                liveMbps));
        }

        stopwatch.Stop();
        if (totalBytes <= 0)
        {
            throw new NetworkSpeedTestException(
                NetworkSpeedTestFailureKind.NodeUnavailable,
                "下载节点返回了空响应，未生成下载速度。 ");
        }

        var mbps = ToMegabitsPerSecond(totalBytes, stopwatch.Elapsed);
        progress?.Report(new NetworkSpeedTestProgress(
            NetworkSpeedTestPhase.Download,
            66,
            $"下载阶段完成 · 实收 {FormatMebibytes(totalBytes):F1} MiB",
            mbps));
        return (mbps, totalBytes);
    }

    private async Task<(double Mbps, long Bytes)> MeasureUploadAsync(
        NetworkSpeedTestNode node,
        IProgress<NetworkSpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new NetworkSpeedTestProgress(
            NetworkSpeedTestPhase.Upload,
            70,
            $"正在上传约 {FormatMebibytes(node.UploadBytes)} MiB 合成零字节…"));

        using var request = new HttpRequestMessage(HttpMethod.Post, node.UploadUri)
        {
            Content = new ByteArrayContent(new byte[node.UploadBytes])
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        ThrowForFailure(response);
        stopwatch.Stop();

        var mbps = ToMegabitsPerSecond(node.UploadBytes, stopwatch.Elapsed);
        progress?.Report(new NetworkSpeedTestProgress(
            NetworkSpeedTestPhase.Upload,
            90,
            $"上传阶段完成 · 已发送 {FormatMebibytes(node.UploadBytes):F1} MiB",
            mbps));
        return (mbps, node.UploadBytes);
    }

    private static void ValidateNode(NetworkSpeedTestNode node)
    {
        if (!node.DownloadUri.IsAbsoluteUri || !node.UploadUri.IsAbsoluteUri ||
            node.DownloadUri.Scheme != Uri.UriSchemeHttps || node.UploadUri.Scheme != Uri.UriSchemeHttps ||
            node.DownloadBytes <= 0 || node.UploadBytes <= 0 ||
            node.DownloadBytes > 32 * 1024 * 1024 || node.UploadBytes > 8 * 1024 * 1024)
        {
            throw new NetworkSpeedTestException(
                NetworkSpeedTestFailureKind.InvalidNode,
                "测速节点必须使用 HTTPS，且流量预算必须位于安全上限内。 ");
        }
    }

    private static void ThrowForFailure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new NetworkSpeedTestException(
                NetworkSpeedTestFailureKind.PermissionDenied,
                $"节点拒绝测速请求（HTTP {(int)response.StatusCode}）。");
        }

        throw new HttpRequestException(
            $"测速节点返回 HTTP {(int)response.StatusCode}。",
            inner: null,
            response.StatusCode);
    }

    private static Uri AddQuery(Uri uri, int bytes)
    {
        var builder = new UriBuilder(uri);
        var existing = builder.Query.TrimStart('?');
        var measurement = $"bytes={bytes}&cacheBust={Guid.NewGuid():N}";
        builder.Query = string.IsNullOrWhiteSpace(existing) ? measurement : existing + "&" + measurement;
        return builder.Uri;
    }

    private static double ToMegabitsPerSecond(long bytes, TimeSpan elapsed)
    {
        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        return bytes * 8d / seconds / 1_000_000d;
    }

    private static double FormatMebibytes(long bytes) => bytes / 1024d / 1024d;

    private static IReadOnlyList<NetworkSpeedTestNode> CreateBuiltInNodes()
    {
        var download = new Uri("https://speed.cloudflare.com/__down");
        var upload = new Uri("https://speed.cloudflare.com/__up");
        return new[]
        {
            new NetworkSpeedTestNode(
                "cloudflare-standard",
                "Cloudflare Edge · 标准",
                "自动就近路由 · 约下载 8 MiB / 上传 2 MiB",
                download,
                upload,
                8 * 1024 * 1024,
                2 * 1024 * 1024),
            new NetworkSpeedTestNode(
                "cloudflare-light",
                "Cloudflare Edge · 省流量",
                "自动就近路由 · 约下载 2 MiB / 上传 512 KiB，结果波动可能更大",
                download,
                upload,
                2 * 1024 * 1024,
                512 * 1024),
            new NetworkSpeedTestNode(
                "cloudflare-extended",
                "Cloudflare Edge · 增强",
                "自动就近路由 · 约下载 16 MiB / 上传 4 MiB，适合稳定网络复测",
                download,
                upload,
                16 * 1024 * 1024,
                4 * 1024 * 1024),
            new NetworkSpeedTestNode(
                "cloudflare-precision",
                "Cloudflare Edge · 高精度",
                "自动就近路由 · 约下载 24 MiB / 上传 6 MiB，耗时更长、流量更多",
                download,
                upload,
                24 * 1024 * 1024,
                6 * 1024 * 1024)
        };
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YuqiEngine-NetworkTest/0.1");
        return client;
    }
}
