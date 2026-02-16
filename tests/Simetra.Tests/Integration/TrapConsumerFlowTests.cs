using System.Diagnostics.Metrics;
using System.Net;
using System.Threading.Channels;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simetra.Devices;
using Simetra.Models;
using Simetra.Pipeline;
using Simetra.Services;

namespace Simetra.Tests.Integration;

/// <summary>
/// End-to-end integration tests proving the complete trap consumer flow:
/// Channel write -> ChannelConsumerService reads -> SnmpExtractorService extracts ->
/// ProcessingCoordinator routes to MetricFactory (Branch A) and StateVectorService (Branch B).
///
/// Wires real service instances in-process (not mocks) for extractor, state vector, metric factory,
/// processing coordinator, and consumer service. Only IDeviceChannelManager and IDeviceRegistry are
/// mocked to control channel wiring without needing full DI infrastructure.
///
/// Satisfies TRAP-07: End-to-end trap consumer pipeline verification.
/// </summary>
public class TrapConsumerFlowTests : IDisposable
{
    private readonly Meter _meter;
    private readonly MeterListener _meterListener;
    private readonly SimetraModule _module;
    private readonly DeviceInfo _simetraDevice;
    private readonly PollDefinitionDto _heartbeatDefinition;
    private readonly SnmpExtractorService _extractor;
    private readonly StateVectorService _stateVector;
    private readonly MetricFactory _metricFactory;
    private readonly ProcessingCoordinator _coordinator;
    private readonly List<(string InstrumentName, long Value)> _recordedMeasurements = [];

    public TrapConsumerFlowTests()
    {
        // Real SimetraModule -- source of truth for heartbeat definition
        _module = new SimetraModule();
        _heartbeatDefinition = _module.TrapDefinitions[0];

        // Real SnmpExtractorService
        _extractor = new SnmpExtractorService(Mock.Of<ILogger<SnmpExtractorService>>());

        // Real StateVectorService
        _stateVector = new StateVectorService(Mock.Of<ILogger<StateVectorService>>());

        // Real MetricFactory with mock IMeterFactory returning a real Meter
        _meter = new Meter("Simetra.Metrics.TrapConsumerFlow");

        var mockMeterFactory = new Mock<IMeterFactory>();
        mockMeterFactory
            .Setup(f => f.Create(It.IsAny<MeterOptions>()))
            .Returns(_meter);

        var siteOptions = Options.Create(new SiteOptions { Name = "e2e-test-site" });

        _metricFactory = new MetricFactory(
            mockMeterFactory.Object,
            siteOptions,
            Mock.Of<ILogger<MetricFactory>>());

        // Real ProcessingCoordinator wiring real MetricFactory + real StateVectorService
        _coordinator = new ProcessingCoordinator(
            _metricFactory,
            _stateVector,
            Mock.Of<ILogger<ProcessingCoordinator>>());

        // DeviceInfo matching the Simetra virtual device
        _simetraDevice = new DeviceInfo(
            _module.DeviceName,
            _module.IpAddress,
            _module.DeviceType,
            _module.TrapDefinitions);

        // MeterListener to capture metric recordings from the test Meter
        _meterListener = new MeterListener();
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Simetra.Metrics.TrapConsumerFlow")
                listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            _recordedMeasurements.Add((instrument.Name, value));
        });
        _meterListener.Start();
    }

    public void Dispose()
    {
        _meterListener.Dispose();
        _meter.Dispose();
    }

    /// <summary>
    /// Creates a bounded channel matching DeviceChannelManager's configuration.
    /// </summary>
    private static Channel<TrapEnvelope> CreateChannel()
    {
        return Channel.CreateBounded<TrapEnvelope>(
            new BoundedChannelOptions(10)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
    }

    /// <summary>
    /// Creates a ChannelConsumerService wired with real service instances and mocked
    /// channel manager/device registry pointing at the provided channel.
    /// </summary>
    private ChannelConsumerService CreateConsumer(
        Channel<TrapEnvelope> channel,
        string deviceName,
        DeviceInfo device)
    {
        var mockChannelManager = new Mock<IDeviceChannelManager>();
        mockChannelManager
            .Setup(m => m.DeviceNames)
            .Returns(new[] { deviceName });
        mockChannelManager
            .Setup(m => m.GetReader(deviceName))
            .Returns(channel.Reader);

        var mockDeviceRegistry = new Mock<IDeviceRegistry>();
        DeviceInfo? outDevice = device;
        mockDeviceRegistry
            .Setup(r => r.TryGetDeviceByName(deviceName, out outDevice))
            .Returns(true);

        return new ChannelConsumerService(
            mockChannelManager.Object,
            mockDeviceRegistry.Object,
            _extractor,
            _coordinator,
            Mock.Of<ILogger<ChannelConsumerService>>());
    }

    /// <summary>
    /// Builds a TrapEnvelope using the SimetraModule's heartbeat definition.
    /// </summary>
    private TrapEnvelope MakeHeartbeatEnvelope(string correlationId)
    {
        return new TrapEnvelope
        {
            Varbinds = new List<Variable>
            {
                new Variable(
                    new ObjectIdentifier(SimetraModule.HeartbeatOid),
                    new Integer32(1))
            },
            SenderAddress = IPAddress.Loopback,
            ReceivedAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
            MatchedDefinition = _heartbeatDefinition
        };
    }

    [Fact]
    public async Task TrapEnvelope_FlowsThrough_ConsumerPipeline_ProducesStateVectorAndMetrics()
    {
        // Arrange: Create channel, consumer, and a heartbeat trap envelope
        var channel = CreateChannel();
        var consumer = CreateConsumer(channel, _module.DeviceName, _simetraDevice);
        var envelope = MakeHeartbeatEnvelope("e2e-test-corr-1");

        // Act: Write envelope to channel, complete channel, start/stop consumer
        await channel.Writer.WriteAsync(envelope);
        channel.Writer.Complete();

        await consumer.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await consumer.StopAsync(CancellationToken.None);

        // Assert: State Vector entry exists with correct data
        var entry = _stateVector.GetEntry(_module.DeviceName, _heartbeatDefinition.MetricName);

        entry.Should().NotBeNull("State Vector should be updated by consumer pipeline");
        entry!.CorrelationId.Should().Be("e2e-test-corr-1",
            "correlationId should propagate through the entire pipeline");
        entry.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5),
            "timestamp should be recent");

        // Assert: Metric recorded with METR-01 naming (PropertyName only)
        _recordedMeasurements.Should().Contain(
            m => m.InstrumentName == "beat",
            "metric name should be PropertyName only (METR-01 convention)");

        // Assert: Metric value correct
        _recordedMeasurements.Should().Contain(
            m => m.InstrumentName == "beat" && m.Value == 1L,
            "heartbeat metric should record value 1");
    }

    [Fact]
    public async Task MultipleTrapEnvelopes_AllProcessed_BeforeChannelCompletes()
    {
        // Arrange: 3 envelopes with different correlationIds
        var channel = CreateChannel();
        var consumer = CreateConsumer(channel, _module.DeviceName, _simetraDevice);

        var envelope1 = MakeHeartbeatEnvelope("corr-1");
        var envelope2 = MakeHeartbeatEnvelope("corr-2");
        var envelope3 = MakeHeartbeatEnvelope("corr-3");

        // Act: Write all 3, complete channel, start/stop consumer
        await channel.Writer.WriteAsync(envelope1);
        await channel.Writer.WriteAsync(envelope2);
        await channel.Writer.WriteAsync(envelope3);
        channel.Writer.Complete();

        await consumer.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await consumer.StopAsync(CancellationToken.None);

        // Assert: State Vector entry exists (last one wins for MetricName)
        var entry = _stateVector.GetEntry(_module.DeviceName, _heartbeatDefinition.MetricName);
        entry.Should().NotBeNull("State Vector should contain entry after processing 3 traps");
        entry!.CorrelationId.Should().Be("corr-3",
            "last write wins -- correlationId should be from the final envelope");

        // Assert: 3 metric measurements recorded (one per envelope)
        _recordedMeasurements.Should().HaveCount(3,
            "each envelope should produce one metric measurement");
        _recordedMeasurements.Should().OnlyContain(
            m => m.InstrumentName == "beat" && m.Value == 1L,
            "all measurements should be beat=1");
    }

    [Fact]
    public async Task UnmatchedTrap_SkippedByConsumer_NoStateVectorUpdate()
    {
        // Arrange: Envelope with MatchedDefinition = null (unmatched trap)
        var channel = CreateChannel();
        var consumer = CreateConsumer(channel, _module.DeviceName, _simetraDevice);

        var envelope = new TrapEnvelope
        {
            Varbinds = new List<Variable>
            {
                new Variable(
                    new ObjectIdentifier("1.3.6.1.4.1.9999.9.9.9.0"),
                    new Integer32(99))
            },
            SenderAddress = IPAddress.Loopback,
            ReceivedAt = DateTimeOffset.UtcNow,
            CorrelationId = "unmatched-corr-1",
            MatchedDefinition = null
        };

        // Act: Write, complete, start/stop consumer
        await channel.Writer.WriteAsync(envelope);
        channel.Writer.Complete();

        await consumer.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await consumer.StopAsync(CancellationToken.None);

        // Assert: No State Vector entry
        var entry = _stateVector.GetEntry(_module.DeviceName, _heartbeatDefinition.MetricName);
        entry.Should().BeNull("unmatched traps should not produce State Vector entries");

        // Assert: No metrics recorded
        _recordedMeasurements.Should().BeEmpty("unmatched traps should not produce metrics");
    }
}
