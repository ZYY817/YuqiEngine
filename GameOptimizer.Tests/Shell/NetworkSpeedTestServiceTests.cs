using System.Net;
using System.Net.Http;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Tests.Shell;

public sealed class NetworkSpeedTestServiceTests
{
    [Fact]
    public void BuiltInNodesUseHttpsAndStayWithinTheDeclaredTrafficBudget()
    {
        using var client = new HttpClient(new RoutingHandler());
        using var service = CreateService(client, () => true);

        Assert.NotEmpty(service.BuiltInNodes);
        Assert.True(service.BuiltInNodes.Count >= 4);
        Assert.All(service.BuiltInNodes, node =>
        {
            Assert.Equal(Uri.UriSchemeHttps, node.DownloadUri.Scheme);
            Assert.Equal(Uri.UriSchemeHttps, node.UploadUri.Scheme);
            Assert.InRange(node.DownloadBytes, 1, 32 * 1024 * 1024);
            Assert.InRange(node.UploadBytes, 1, 8 * 1024 * 1024);
        });
    }

    [Fact]
    public async Task RunAsyncUsesTransferredBytesAndCompletesEveryMeasurementPhase()
    {
        var handler = new RoutingHandler { ExtraDownloadBytes = 4096 };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var service = CreateService(client, () => true);
        var progress = new RecordingProgress();

        var result = await service.RunAsync(service.BuiltInNodes[1], progress, CancellationToken.None);

        Assert.True(result.AverageLatencyMs >= 0);
        Assert.True(result.DnsResolutionMs >= 0);
        Assert.True(result.NodeReachable);
        Assert.True(result.MinimumLatencyMs >= 0);
        Assert.Equal(0, result.PacketLossPercent);
        Assert.True(result.DownloadMbps > 0);
        Assert.True(result.UploadMbps > 0);
        Assert.Equal(service.BuiltInNodes[1].DownloadBytes, result.DownloadedBytes);
        Assert.Equal(service.BuiltInNodes[1].UploadBytes, result.UploadedBytes);
        Assert.Equal(service.BuiltInNodes[1].UploadBytes, handler.UploadedBytes);
        Assert.Contains(progress.Items, item => item.Phase == NetworkSpeedTestPhase.Latency);
        Assert.Contains(progress.Items, item => item.Phase == NetworkSpeedTestPhase.Download);
        Assert.Contains(progress.Items, item => item.Phase == NetworkSpeedTestPhase.Upload);
        Assert.Equal(100, progress.Items[^1].Percent);
    }

    [Fact]
    public async Task RunAsyncCancelsAnInFlightRequestWithoutRetrying()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var service = CreateService(client, () => true);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RunAsync(service.BuiltInNodes[0], progress: null, cts.Token));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task RunAsyncReportsOfflineBeforeSendingAnyRequest()
    {
        var handler = new RoutingHandler();
        using var client = new HttpClient(handler);
        using var service = CreateService(client, () => false);

        var error = await Assert.ThrowsAsync<NetworkSpeedTestException>(() =>
            service.RunAsync(service.BuiltInNodes[0], progress: null, CancellationToken.None));

        Assert.Equal(NetworkSpeedTestFailureKind.Offline, error.Kind);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task RunAsyncClassifiesHttpForbiddenAsPermissionDenied()
    {
        using var client = new HttpClient(new ForbiddenHandler());
        using var service = CreateService(client, () => true);

        var error = await Assert.ThrowsAsync<NetworkSpeedTestException>(() =>
            service.RunAsync(service.BuiltInNodes[0], progress: null, CancellationToken.None));

        Assert.Equal(NetworkSpeedTestFailureKind.PermissionDenied, error.Kind);
    }

    [Theory]
    [InlineData("http://speed.example.com/")]
    [InlineData("https://user:secret@speed.example.com/")]
    [InlineData("not-a-url")]
    public void CustomNodeRejectsInsecureOrCredentialBearingAddresses(string input)
    {
        using var client = new HttpClient(new RoutingHandler());
        using var service = CreateService(client, () => true);

        Assert.False(service.TryCreateCustomNode(input, out var node, out var error));
        Assert.Null(node);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void CustomNodeDerivesCompatibleHttpsEndpoints()
    {
        using var client = new HttpClient(new RoutingHandler());
        using var service = CreateService(client, () => true);

        Assert.True(service.TryCreateCustomNode(
            "https://speed.example.com/base",
            out var node,
            out var error));

        Assert.Empty(error);
        Assert.NotNull(node);
        Assert.Equal("https://speed.example.com/base/__down", node.DownloadUri.AbsoluteUri);
        Assert.Equal("https://speed.example.com/base/__up", node.UploadUri.AbsoluteUri);
    }

    private sealed class RecordingProgress : IProgress<NetworkSpeedTestProgress>
    {
        public List<NetworkSpeedTestProgress> Items { get; } = new();
        public void Report(NetworkSpeedTestProgress value) => Items.Add(value);
    }

    private static NetworkSpeedTestService CreateService(HttpClient client, Func<bool> networkAvailable) =>
        new(client, networkAvailable, dnsResolver: static (_, _) =>
            Task.FromResult(new[] { IPAddress.Loopback }));

    private sealed class RoutingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public int UploadedBytes { get; private set; }
        public int ExtraDownloadBytes { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Method == HttpMethod.Post)
            {
                UploadedBytes = (await request.Content!.ReadAsByteArrayAsync(cancellationToken)).Length;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                };
            }

            var bytesValue = request.RequestUri!.Query
                .TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .FirstOrDefault(part => part.Length == 2 && part[0] == "bytes")?
                .ElementAtOrDefault(1);
            var bytes = int.TryParse(bytesValue, out var parsed) ? parsed : 0;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[bytes + (bytes > 0 ? ExtraDownloadBytes : 0)])
            };
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation should stop the request.");
        }
    }

    private sealed class ForbiddenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
    }
}
