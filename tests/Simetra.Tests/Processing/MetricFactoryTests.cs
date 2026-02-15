using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Simetra.Configuration;
using Simetra.Models;
using Simetra.Pipeline;

namespace Simetra.Tests.Processing;

public class MetricFactoryTests : IDisposable
{
    private readonly Meter _meter;
    private readonly MetricFactory _sut;
    private readonly MeterListener _listener;
    private readonly List<KeyValuePair<string, object?>> _recordedTags = [];
    private readonly List<(string InstrumentName, string InstrumentType, long Value)> _recordedMeasurements = [];

    public MetricFactoryTests()
    {
        _meter = new Meter("Simetra.Metrics.Test");

        var mockMeterFactory = new Mock<IMeterFactory>();
        mockMeterFactory
            .Setup(f => f.Create(It.IsAny<MeterOptions>()))
            .Returns(_meter);

        var siteOptions = Options.Create(new SiteOptions { Name = "test-site" });

        _sut = new MetricFactory(
            mockMeterFactory.Object,
            siteOptions,
            Mock.Of<ILogger<MetricFactory>>());

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Simetra.Metrics.Test")
                meterListener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            _recordedMeasurements.Add((instrument.Name, instrument.GetType().Name, value));
            foreach (var tag in tags)
                _recordedTags.Add(tag);
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meter.Dispose();
    }

    private static DeviceInfo MakeDevice()
    {
        return new DeviceInfo(
            "router-core-1",
            "10.0.0.1",
            "router",
            Array.Empty<PollDefinitionDto>().ToList().AsReadOnly());
    }

    private static ExtractionResult MakeResult(
        string metricName,
        MetricType metricType,
        Dictionary<string, long>? metrics = null,
        Dictionary<string, string>? labels = null)
    {
        var definition = new PollDefinitionDto(
            metricName,
            metricType,
            new List<OidEntryDto>
            {
                new("1.3.6.1.2.1.1.0", "value", OidRole.Metric, null)
            }.AsReadOnly(),
            30,
            MetricPollSource.Module);

        return new ExtractionResult
        {
            Definition = definition,
            Metrics = new ReadOnlyDictionary<string, long>(
                metrics ?? new Dictionary<string, long> { { "value", 42L } }),
            Labels = new ReadOnlyDictionary<string, string>(
                labels ?? new Dictionary<string, string>())
        };
    }

    [Fact]
    public void RecordMetrics_IncludesBaseLabels()
    {
        var result = MakeResult("simetra_cpu", MetricType.Gauge);
        var device = MakeDevice();

        _sut.RecordMetrics(result, device);
        _listener.RecordObservableInstruments();

        _recordedTags.Should().Contain(t => t.Key == "site" && (string)t.Value! == "test-site");
        _recordedTags.Should().Contain(t => t.Key == "device_name" && (string)t.Value! == "router-core-1");
        _recordedTags.Should().Contain(t => t.Key == "device_ip" && (string)t.Value! == "10.0.0.1");
        _recordedTags.Should().Contain(t => t.Key == "device_type" && (string)t.Value! == "router");
    }

    [Fact]
    public void RecordMetrics_AppendsDynamicLabels()
    {
        var result = MakeResult("simetra_cpu", MetricType.Gauge,
            labels: new Dictionary<string, string> { { "status", "up" } });
        var device = MakeDevice();

        _sut.RecordMetrics(result, device);
        _listener.RecordObservableInstruments();

        // Verify dynamic label present alongside base labels
        _recordedTags.Should().Contain(t => t.Key == "status" && (string)t.Value! == "up");
        // Also verify base labels are still present (total 5 tags: 4 base + 1 dynamic)
        _recordedTags.Should().Contain(t => t.Key == "site");
        _recordedTags.Should().Contain(t => t.Key == "device_name");
        _recordedTags.Should().Contain(t => t.Key == "device_ip");
        _recordedTags.Should().Contain(t => t.Key == "device_type");
    }

    [Fact]
    public void RecordMetrics_MetricNameFormat()
    {
        var result = MakeResult("simetra_heartbeat", MetricType.Gauge,
            metrics: new Dictionary<string, long> { { "uptime", 100L } });
        var device = MakeDevice();

        _sut.RecordMetrics(result, device);
        _listener.RecordObservableInstruments();

        _recordedMeasurements.Should().Contain(m => m.InstrumentName == "simetra_heartbeat_uptime");
    }

    [Fact]
    public void RecordMetrics_GaugeType_RecordsValue()
    {
        var result = MakeResult("simetra_gauge_test", MetricType.Gauge,
            metrics: new Dictionary<string, long> { { "value", 85L } });
        var device = MakeDevice();

        _sut.RecordMetrics(result, device);
        _listener.RecordObservableInstruments();

        _recordedMeasurements.Should().Contain(m =>
            m.InstrumentName == "simetra_gauge_test_value" &&
            m.InstrumentType.Contains("Gauge") &&
            m.Value == 85L);
    }

    [Fact]
    public void RecordMetrics_CounterType_AddsValue()
    {
        var result = MakeResult("simetra_counter_test", MetricType.Counter,
            metrics: new Dictionary<string, long> { { "value", 100L } });
        var device = MakeDevice();

        _sut.RecordMetrics(result, device);
        _listener.RecordObservableInstruments();

        _recordedMeasurements.Should().Contain(m =>
            m.InstrumentName == "simetra_counter_test_value" &&
            m.InstrumentType.Contains("Counter") &&
            m.Value == 100L);
    }
}
