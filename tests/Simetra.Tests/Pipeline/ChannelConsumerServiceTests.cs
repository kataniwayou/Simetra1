using System.Net;
using System.Threading.Channels;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
using Simetra.Models;
using Simetra.Pipeline;
using Simetra.Services;

namespace Simetra.Tests.Pipeline;

public class ChannelConsumerServiceTests
{
    private readonly Mock<IDeviceChannelManager> _mockChannelManager = new();
    private readonly Mock<IDeviceRegistry> _mockDeviceRegistry = new();
    private readonly Mock<ISnmpExtractor> _mockExtractor = new();
    private readonly Mock<IProcessingCoordinator> _mockCoordinator = new();
    private readonly Mock<ILogger<ChannelConsumerService>> _mockLogger = new();

    private static readonly DeviceInfo TestDevice = new(
        "test-device",
        "10.0.0.1",
        "router",
        Array.Empty<PollDefinitionDto>().ToList().AsReadOnly());

    private static readonly PollDefinitionDto TestDefinition = new(
        "test_metric",
        MetricType.Gauge,
        new List<OidEntryDto>
        {
            new("1.3.6.1.2.1.1.0", "value", OidRole.Metric, null)
        }.AsReadOnly(),
        30,
        MetricPollSource.Module);

    private ChannelConsumerService CreateSut()
    {
        return new ChannelConsumerService(
            _mockChannelManager.Object,
            _mockDeviceRegistry.Object,
            _mockExtractor.Object,
            _mockCoordinator.Object,
            _mockLogger.Object);
    }

    private static TrapEnvelope MakeEnvelope(
        PollDefinitionDto? definition = null,
        string correlationId = "test-corr-1")
    {
        return new TrapEnvelope
        {
            Varbinds = new List<Variable>
            {
                new(new ObjectIdentifier("1.3.6.1.2.1.1.0"), new Integer32(42))
            },
            SenderAddress = IPAddress.Loopback,
            ReceivedAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
            MatchedDefinition = definition ?? TestDefinition
        };
    }

    private static Channel<TrapEnvelope> CreateChannel()
    {
        return Channel.CreateBounded<TrapEnvelope>(
            new BoundedChannelOptions(10)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
    }

    private void SetupSingleDevice(
        string deviceName,
        ChannelReader<TrapEnvelope> reader,
        DeviceInfo? device = null)
    {
        _mockChannelManager
            .Setup(m => m.DeviceNames)
            .Returns(new[] { deviceName });
        _mockChannelManager
            .Setup(m => m.GetReader(deviceName))
            .Returns(reader);

        var resolvedDevice = device ?? TestDevice;
        _mockDeviceRegistry
            .Setup(r => r.TryGetDeviceByName(deviceName, out resolvedDevice))
            .Returns(true);
    }

    [Fact]
    public async Task ConsumesEnvelopeFromChannel_CallsExtractorAndCoordinator()
    {
        // Arrange
        var channel = CreateChannel();
        SetupSingleDevice("test-device", channel.Reader);

        var envelope = MakeEnvelope();
        var extractionResult = new ExtractionResult { Definition = TestDefinition };

        _mockExtractor
            .Setup(e => e.Extract(envelope.Varbinds, TestDefinition))
            .Returns(extractionResult);

        await channel.Writer.WriteAsync(envelope);
        channel.Writer.Complete();

        // Act
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);
        // Wait for processing to complete (channel is pre-completed, so consumer finishes quickly)
        await Task.Delay(200);
        await sut.StopAsync(CancellationToken.None);

        // Assert
        _mockExtractor.Verify(
            e => e.Extract(envelope.Varbinds, TestDefinition),
            Times.Once);
        _mockCoordinator.Verify(
            c => c.Process(extractionResult, TestDevice, "test-corr-1"),
            Times.Once);
    }

    [Fact]
    public async Task SkipsEnvelopeWithNullMatchedDefinition()
    {
        // Arrange
        var channel = CreateChannel();
        SetupSingleDevice("test-device", channel.Reader);

        var envelope = MakeEnvelope(definition: null);
        envelope.MatchedDefinition = null; // Explicitly null

        await channel.Writer.WriteAsync(envelope);
        channel.Writer.Complete();

        // Act
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);
        await Task.Delay(200);
        await sut.StopAsync(CancellationToken.None);

        // Assert
        _mockExtractor.Verify(
            e => e.Extract(It.IsAny<IList<Variable>>(), It.IsAny<PollDefinitionDto>()),
            Times.Never);
        _mockCoordinator.Verify(
            c => c.Process(
                It.IsAny<ExtractionResult>(),
                It.IsAny<DeviceInfo>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ContinuesProcessingAfterSingleTrapError()
    {
        // Arrange
        var channel = CreateChannel();
        SetupSingleDevice("test-device", channel.Reader);

        var envelope1 = MakeEnvelope(correlationId: "corr-1");
        var envelope2 = MakeEnvelope(correlationId: "corr-2");

        var extractionResult = new ExtractionResult { Definition = TestDefinition };

        // First call throws, second succeeds
        var callCount = 0;
        _mockExtractor
            .Setup(e => e.Extract(It.IsAny<IList<Variable>>(), TestDefinition))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("Extraction failed");
                return extractionResult;
            });

        await channel.Writer.WriteAsync(envelope1);
        await channel.Writer.WriteAsync(envelope2);
        channel.Writer.Complete();

        // Act
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);
        await Task.Delay(200);
        await sut.StopAsync(CancellationToken.None);

        // Assert: Coordinator called once for the second (successful) envelope
        _mockCoordinator.Verify(
            c => c.Process(extractionResult, TestDevice, "corr-2"),
            Times.Once);
        // Extractor was called twice (first threw, second succeeded)
        _mockExtractor.Verify(
            e => e.Extract(It.IsAny<IList<Variable>>(), TestDefinition),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CompletesGracefully_WhenChannelCompleted()
    {
        // Arrange: Channel with no items, immediately completed
        var channel = CreateChannel();
        SetupSingleDevice("test-device", channel.Reader);
        channel.Writer.Complete();

        // Act
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);
        await Task.Delay(200);

        // Assert: StopAsync completes without throwing
        var act = () => sut.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandlesMultipleDeviceChannels()
    {
        // Arrange: Two devices with separate channels
        var channelA = CreateChannel();
        var channelB = CreateChannel();

        var deviceA = new DeviceInfo(
            "device-a", "10.0.0.1", "router",
            Array.Empty<PollDefinitionDto>().ToList().AsReadOnly());
        var deviceB = new DeviceInfo(
            "device-b", "10.0.0.2", "switch",
            Array.Empty<PollDefinitionDto>().ToList().AsReadOnly());

        _mockChannelManager
            .Setup(m => m.DeviceNames)
            .Returns(new[] { "device-a", "device-b" });
        _mockChannelManager
            .Setup(m => m.GetReader("device-a"))
            .Returns(channelA.Reader);
        _mockChannelManager
            .Setup(m => m.GetReader("device-b"))
            .Returns(channelB.Reader);

        _mockDeviceRegistry
            .Setup(r => r.TryGetDeviceByName("device-a", out deviceA))
            .Returns(true);
        _mockDeviceRegistry
            .Setup(r => r.TryGetDeviceByName("device-b", out deviceB))
            .Returns(true);

        var extractionResult = new ExtractionResult { Definition = TestDefinition };
        _mockExtractor
            .Setup(e => e.Extract(It.IsAny<IList<Variable>>(), TestDefinition))
            .Returns(extractionResult);

        var envelopeA = MakeEnvelope(correlationId: "corr-a");
        var envelopeB = MakeEnvelope(correlationId: "corr-b");

        await channelA.Writer.WriteAsync(envelopeA);
        await channelB.Writer.WriteAsync(envelopeB);
        channelA.Writer.Complete();
        channelB.Writer.Complete();

        // Act
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);
        await Task.Delay(300);
        await sut.StopAsync(CancellationToken.None);

        // Assert: Extractor called twice (once per device)
        _mockExtractor.Verify(
            e => e.Extract(It.IsAny<IList<Variable>>(), TestDefinition),
            Times.Exactly(2));
        // Coordinator called for each device
        _mockCoordinator.Verify(
            c => c.Process(extractionResult, deviceA, "corr-a"),
            Times.Once);
        _mockCoordinator.Verify(
            c => c.Process(extractionResult, deviceB, "corr-b"),
            Times.Once);
    }
}
