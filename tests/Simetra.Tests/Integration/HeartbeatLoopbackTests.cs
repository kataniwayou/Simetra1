using System.Diagnostics.Metrics;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simetra.Devices;
using Simetra.Models;
using Simetra.Pipeline;
using Simetra.Services;

namespace Simetra.Tests.Integration;

/// <summary>
/// End-to-end integration tests proving the heartbeat data flow through the full pipeline:
/// SNMP varbinds -> SnmpExtractorService -> ProcessingCoordinator (Branch A: metrics, Branch B: State Vector).
/// Wires real service instances in-process -- no UDP, no Quartz, just data flow.
/// </summary>
public class HeartbeatLoopbackTests : IDisposable
{
    // Device identity for the Simetra virtual device (matches appsettings.json)
    private const string TestDeviceName = "simetra-supervisor";
    private const string TestDeviceIp = "127.0.0.1";

    private readonly Meter _meter;
    private readonly MeterListener _listener;
    private readonly SnmpExtractorService _extractor;
    private readonly StateVectorService _stateVector;
    private readonly MetricFactory _metricFactory;
    private readonly ProcessingCoordinator _coordinator;
    private readonly SimetraModule _module;
    private readonly DeviceInfo _simetraDevice;
    private readonly PollDefinitionDto _heartbeatDefinition;
    private readonly List<(string InstrumentName, long Value)> _recordedMeasurements = [];

    public HeartbeatLoopbackTests()
    {
        // Real SimetraModule -- source of truth for heartbeat definition
        _module = new SimetraModule();
        _heartbeatDefinition = _module.TrapDefinitions[0];

        // Real SnmpExtractorService with mock logger
        _extractor = new SnmpExtractorService(Mock.Of<ILogger<SnmpExtractorService>>());

        // Real StateVectorService with mock logger
        _stateVector = new StateVectorService(Mock.Of<ILogger<StateVectorService>>());

        // Real MetricFactory with mock IMeterFactory returning a real Meter
        _meter = new Meter("Simetra.Metrics.Integration");

        var mockMeterFactory = new Mock<IMeterFactory>();
        mockMeterFactory
            .Setup(f => f.Create(It.IsAny<MeterOptions>()))
            .Returns(_meter);

        var siteOptions = Options.Create(new SiteOptions { Name = "integration-test-site" });

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
            TestDeviceName,
            TestDeviceIp,
            _module.DeviceType,
            _module.TrapDefinitions);

        // MeterListener to capture metric recordings
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Simetra.Metrics.Integration")
                meterListener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            _recordedMeasurements.Add((instrument.Name, value));
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meter.Dispose();
    }

    [Fact]
    public void HeartbeatData_FlowsThroughPipeline_ProducesStateVectorEntry()
    {
        // Arrange: Build SNMP varbinds matching the heartbeat OID (value=1 for alive)
        var varbinds = new List<Variable>
        {
            new Variable(
                new ObjectIdentifier(SimetraModule.HeartbeatOid),
                new Integer32(1))
        };

        // Act Step 1: Extract varbinds through the extractor
        var result = _extractor.Extract(varbinds, _heartbeatDefinition);

        // Assert extraction produced metric data
        result.Metrics.Should().NotBeEmpty("extraction should produce at least one metric");

        // Act Step 2: Process through the coordinator (both branches)
        _coordinator.Process(result, _simetraDevice, "test-corr-1");

        // Assert Step 3: State Vector entry exists with correct data
        var entry = _stateVector.GetEntry(TestDeviceName, _heartbeatDefinition.MetricName);

        entry.Should().NotBeNull("State Vector should contain entry after processing Module-sourced data");
        entry!.CorrelationId.Should().Be("test-corr-1", "correlation ID should propagate through the pipeline");
        entry.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5),
            "timestamp should be recent");
        entry.Result.Should().BeSameAs(result, "State Vector should store the extraction result");
    }

    [Fact]
    public void HeartbeatExtraction_ProducesMetricValues()
    {
        // Arrange: Build SNMP varbinds matching the heartbeat OID
        var varbinds = new List<Variable>
        {
            new Variable(
                new ObjectIdentifier(SimetraModule.HeartbeatOid),
                new Integer32(1))
        };

        // Act: Extract varbinds
        var result = _extractor.Extract(varbinds, _heartbeatDefinition);

        // Assert: ExtractionResult contains the "beat" property with numeric value 1
        result.Metrics.Should().ContainKey("beat", "heartbeat OID property name is 'beat'");
        result.Metrics["beat"].Should().Be(1L, "heartbeat alive value should be 1");

        // Assert: Definition is carried through
        result.Definition.Should().BeSameAs(_heartbeatDefinition);
        result.Definition.MetricName.Should().Be("simetra_heartbeat");
        result.Definition.Source.Should().Be(MetricPollSource.Module);
    }
}
