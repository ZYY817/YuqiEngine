using System.Text.Json;
using GameOptimizer.Core.SystemRestore;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Tests.SystemRestore;

public sealed class SystemRestorePointWriterTests
{
    [Fact]
    public void WriterPairsBeginAndEndAndReturnsSequence()
    {
        var native = new FakeNative
        {
            BeginResult = new(true, 0, 77),
            EndResult = new(true, 0, 77)
        };

        var response = RunWriter(native, CreateCommand());

        Assert.Equal(SystemRestorePointMutationOutcome.Succeeded, response.Outcome);
        Assert.Equal(SystemRestorePointMutationState.Changed, response.MutationState);
        Assert.Equal(77, response.SequenceNumber);
        Assert.Equal(["BEGIN:test point", "END:77:test point"], native.Calls);
    }

    [Fact]
    public void BeginFailureIsProvenNotChangedAndDoesNotCallEnd()
    {
        var native = new FakeNative
        {
            BeginResult = new(false, 5, 0)
        };

        var response = RunWriter(native, CreateCommand());

        Assert.Equal(SystemRestorePointMutationOutcome.Failed, response.Outcome);
        Assert.Equal(SystemRestorePointMutationState.NotChanged, response.MutationState);
        Assert.Equal(5, response.NativeStatus);
        Assert.Single(native.Calls);
    }

    [Fact]
    public void EndFailureRemainsUnknownForOwnerReadbackAndReconciliation()
    {
        var native = new FakeNative
        {
            BeginResult = new(true, 0, 88),
            EndResult = new(false, 13, 88)
        };

        var response = RunWriter(native, CreateCommand());

        Assert.Equal(SystemRestorePointMutationOutcome.Unknown, response.Outcome);
        Assert.Equal(SystemRestorePointMutationState.Unknown, response.MutationState);
        Assert.Equal(88, response.SequenceNumber);
        Assert.Equal(13, response.NativeStatus);
    }

    [Fact]
    public void WriterRejectsTamperedPlanAndVolumeBeforeNativeInvocation()
    {
        var native = new FakeNative();
        var badHash = CreateCommand() with { PlanHash = "BAD" };
        var badHashResponse = RunWriter(native, badHash);
        Assert.Equal(SystemRestorePointMutationState.NotChanged, badHashResponse.MutationState);
        Assert.Empty(native.Calls);

        var wrongVolume = CreateCommand() with { VolumeIdentity = "other-volume" };
        var wrongVolumeResponse = RunWriter(native, wrongVolume);
        Assert.Equal(SystemRestorePointMutationState.NotChanged, wrongVolumeResponse.MutationState);
        Assert.Empty(native.Calls);
    }

    [Fact]
    public async Task FileProtocolBoundaryCarriesTypedResultWithoutCallingRealRestoreApi()
    {
        var directory = Path.Combine(Path.GetTempPath(), "YuqiEngineWriterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var requestPath = Path.Combine(directory, "request.json");
            var responsePath = Path.Combine(directory, "response.json");
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new SystemRestorePointWriterRequest(
                SystemRestorePointWriterRequest.CurrentProtocolVersion,
                CreateCommand())));
            var native = new FakeNative
            {
                BeginResult = new(true, 0, 99),
                EndResult = new(true, 0, 99)
            };

            var exitCode = SystemRestorePointWriterEntryPoint.RunFiles(requestPath, responsePath, native);
            var response = JsonSerializer.Deserialize<SystemRestorePointWriterResponse>(
                await File.ReadAllTextAsync(responsePath));

            Assert.Equal(0, exitCode);
            Assert.NotNull(response);
            Assert.Equal(SystemRestorePointMutationOutcome.Succeeded, response.Outcome);
            Assert.Equal(99, response.SequenceNumber);
            Assert.Equal(2, native.Calls.Count);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static SystemRestorePointWriterResponse RunWriter(
        FakeNative native,
        SystemRestorePointCreateCommand command)
    {
        var request = JsonSerializer.Serialize(new SystemRestorePointWriterRequest(
            SystemRestorePointWriterRequest.CurrentProtocolVersion,
            command));
        using var input = new StringReader(request);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = SystemRestorePointWriterEntryPoint.Run(input, output, error, native);

        Assert.Equal(0, exitCode);
        return Assert.IsType<SystemRestorePointWriterResponse>(
            JsonSerializer.Deserialize<SystemRestorePointWriterResponse>(output.ToString()));
    }

    private static SystemRestorePointCreateCommand CreateCommand() => new(
        Guid.NewGuid(),
        new string('A', 64),
        "test-volume",
        "test point");

    private sealed class FakeNative : SystemRestorePointWriterEntryPoint.ISystemRestoreNativeApi
    {
        public bool IsWindows { get; init; } = true;
        public bool IsAdministrator { get; init; } = true;
        public string VolumeIdentity { get; init; } = "test-volume";
        public SystemRestorePointWriterEntryPoint.NativeCallResult BeginResult { get; init; } = new(true, 0, 1);
        public SystemRestorePointWriterEntryPoint.NativeCallResult EndResult { get; init; } = new(true, 0, 1);
        public List<string> Calls { get; } = [];

        public bool TryGetSystemVolumeIdentity(out string identity)
        {
            identity = VolumeIdentity;
            return true;
        }

        public SystemRestorePointWriterEntryPoint.NativeCallResult Begin(string description)
        {
            Calls.Add($"BEGIN:{description}");
            return BeginResult;
        }

        public SystemRestorePointWriterEntryPoint.NativeCallResult End(string description, long sequenceNumber)
        {
            Calls.Add($"END:{sequenceNumber}:{description}");
            return EndResult;
        }
    }
}
