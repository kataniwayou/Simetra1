using Microsoft.Extensions.Configuration;

namespace Simetra.Tests.Configuration;

/// <summary>
/// End-to-end tests proving IConfiguration binds JSON values into strongly typed Options objects.
/// Uses in-memory configuration dictionaries for isolation (no file dependencies).
/// </summary>
public sealed class ConfigurationBindingTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [Fact]
    public void Bind_WithCompleteValidJson_PopulatesAllSiteProperties()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Site:Name"] = "site-nyc-01",
            ["Site:PodIdentity"] = "pod-abc-123",
        });

        var options = new SiteOptions { Name = "" };
        config.GetSection(SiteOptions.SectionName).Bind(options);

        options.Name.Should().Be("site-nyc-01");
        options.PodIdentity.Should().Be("pod-abc-123");
    }

    [Fact]
    public void Bind_WithCompleteValidJson_PopulatesAllLeaseProperties()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Lease:Name"] = "my-lease",
            ["Lease:Namespace"] = "my-ns",
            ["Lease:RenewIntervalSeconds"] = "5",
            ["Lease:DurationSeconds"] = "20",
        });

        var options = new LeaseOptions { Name = "", Namespace = "" };
        config.GetSection(LeaseOptions.SectionName).Bind(options);

        options.Name.Should().Be("my-lease");
        options.Namespace.Should().Be("my-ns");
        options.RenewIntervalSeconds.Should().Be(5);
        options.DurationSeconds.Should().Be(20);
    }

    [Fact]
    public void Bind_WithCompleteValidJson_PopulatesAllSnmpListenerProperties()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SnmpListener:BindAddress"] = "192.168.1.1",
            ["SnmpListener:Port"] = "1162",
            ["SnmpListener:CommunityString"] = "secret",
            ["SnmpListener:Version"] = "v2c",
        });

        var options = new SnmpListenerOptions { BindAddress = "", CommunityString = "", Version = "" };
        config.GetSection(SnmpListenerOptions.SectionName).Bind(options);

        options.BindAddress.Should().Be("192.168.1.1");
        options.Port.Should().Be(1162);
        options.CommunityString.Should().Be("secret");
        options.Version.Should().Be("v2c");
    }

    [Fact]
    public void Bind_WithCompleteValidJson_PopulatesDevicesWithNestedMetricPolls()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Devices:0:Name"] = "router-core-1",
            ["Devices:0:IpAddress"] = "10.0.1.1",
            ["Devices:0:DeviceType"] = "router",
            ["Devices:0:MetricPolls:0:MetricName"] = "simetra_cpu",
            ["Devices:0:MetricPolls:0:MetricType"] = "Gauge",
            ["Devices:0:MetricPolls:0:IntervalSeconds"] = "30",
            ["Devices:0:MetricPolls:0:Oids:0:Oid"] = "1.3.6.1.4.1.9999.1.3.1.0",
            ["Devices:0:MetricPolls:0:Oids:0:PropertyName"] = "cpu_utilization",
            ["Devices:0:MetricPolls:0:Oids:0:Role"] = "Metric",
            ["Devices:1:Name"] = "switch-floor-2",
            ["Devices:1:IpAddress"] = "10.0.2.1",
            ["Devices:1:DeviceType"] = "switch",
        });

        var devicesOptions = new DevicesOptions();
        config.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices);

        devicesOptions.Devices.Should().HaveCount(2);

        var device0 = devicesOptions.Devices[0];
        device0.Name.Should().Be("router-core-1");
        device0.IpAddress.Should().Be("10.0.1.1");
        device0.DeviceType.Should().Be("router");
        device0.MetricPolls.Should().HaveCount(1);

        var poll0 = device0.MetricPolls[0];
        poll0.MetricName.Should().Be("simetra_cpu");
        poll0.MetricType.Should().Be(MetricType.Gauge);
        poll0.IntervalSeconds.Should().Be(30);
        poll0.Oids.Should().HaveCount(1);

        var oid0 = poll0.Oids[0];
        oid0.Oid.Should().Be("1.3.6.1.4.1.9999.1.3.1.0");
        oid0.PropertyName.Should().Be("cpu_utilization");
        oid0.Role.Should().Be(OidRole.Metric);

        var device1 = devicesOptions.Devices[1];
        device1.Name.Should().Be("switch-floor-2");
        device1.MetricPolls.Should().BeEmpty();
    }

    [Fact]
    public void Bind_WithDeviceMetricPolls_DeserializesOidEntriesWithRoles()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Devices:0:Name"] = "router-1",
            ["Devices:0:IpAddress"] = "10.0.1.1",
            ["Devices:0:DeviceType"] = "router",
            ["Devices:0:MetricPolls:0:MetricName"] = "simetra_memory",
            ["Devices:0:MetricPolls:0:MetricType"] = "Gauge",
            ["Devices:0:MetricPolls:0:IntervalSeconds"] = "60",
            ["Devices:0:MetricPolls:0:Oids:0:Oid"] = "1.3.6.1.4.1.9999.1.3.2.0",
            ["Devices:0:MetricPolls:0:Oids:0:PropertyName"] = "memory_used_bytes",
            ["Devices:0:MetricPolls:0:Oids:0:Role"] = "Metric",
            ["Devices:0:MetricPolls:0:Oids:1:Oid"] = "1.3.6.1.4.1.9999.1.1.1.0",
            ["Devices:0:MetricPolls:0:Oids:1:PropertyName"] = "hostname",
            ["Devices:0:MetricPolls:0:Oids:1:Role"] = "Label",
        });

        var devicesOptions = new DevicesOptions();
        config.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices);

        var oids = devicesOptions.Devices[0].MetricPolls[0].Oids;
        oids.Should().HaveCount(2);
        oids[0].Role.Should().Be(OidRole.Metric);
        oids[1].Role.Should().Be(OidRole.Label);
    }

    [Fact]
    public void Bind_WithDeviceMetricPolls_DeserializesMetricTypeFromString()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Devices:0:Name"] = "router-1",
            ["Devices:0:IpAddress"] = "10.0.1.1",
            ["Devices:0:DeviceType"] = "router",
            ["Devices:0:MetricPolls:0:MetricName"] = "simetra_cpu",
            ["Devices:0:MetricPolls:0:MetricType"] = "Gauge",
            ["Devices:0:MetricPolls:0:IntervalSeconds"] = "30",
            ["Devices:0:MetricPolls:0:Oids:0:Oid"] = "1.3.6.1.4.1.9999.1.3.1.0",
            ["Devices:0:MetricPolls:0:Oids:0:PropertyName"] = "cpu_util",
            ["Devices:0:MetricPolls:0:Oids:0:Role"] = "Metric",
            ["Devices:0:MetricPolls:1:MetricName"] = "simetra_bytes",
            ["Devices:0:MetricPolls:1:MetricType"] = "Counter",
            ["Devices:0:MetricPolls:1:IntervalSeconds"] = "60",
            ["Devices:0:MetricPolls:1:Oids:0:Oid"] = "1.3.6.1.4.1.9999.2.1.0",
            ["Devices:0:MetricPolls:1:Oids:0:PropertyName"] = "bytes_total",
            ["Devices:0:MetricPolls:1:Oids:0:Role"] = "Metric",
        });

        var devicesOptions = new DevicesOptions();
        config.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices);

        var polls = devicesOptions.Devices[0].MetricPolls;
        polls.Should().HaveCount(2);
        polls[0].MetricType.Should().Be(MetricType.Gauge);
        polls[1].MetricType.Should().Be(MetricType.Counter);
    }

    [Fact]
    public void Bind_WithEnumMap_DeserializesDictionaryCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Devices:0:Name"] = "router-1",
            ["Devices:0:IpAddress"] = "10.0.1.1",
            ["Devices:0:DeviceType"] = "router",
            ["Devices:0:MetricPolls:0:MetricName"] = "simetra_status",
            ["Devices:0:MetricPolls:0:MetricType"] = "Gauge",
            ["Devices:0:MetricPolls:0:IntervalSeconds"] = "30",
            ["Devices:0:MetricPolls:0:Oids:0:Oid"] = "1.3.6.1.4.1.9999.1.5.1.0",
            ["Devices:0:MetricPolls:0:Oids:0:PropertyName"] = "status",
            ["Devices:0:MetricPolls:0:Oids:0:Role"] = "Label",
            ["Devices:0:MetricPolls:0:Oids:0:EnumMap:1"] = "up",
            ["Devices:0:MetricPolls:0:Oids:0:EnumMap:2"] = "down",
            ["Devices:0:MetricPolls:0:Oids:0:EnumMap:3"] = "testing",
        });

        var devicesOptions = new DevicesOptions();
        config.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices);

        var enumMap = devicesOptions.Devices[0].MetricPolls[0].Oids[0].EnumMap;
        enumMap.Should().NotBeNull();
        enumMap.Should().HaveCount(3);
        enumMap![1].Should().Be("up");
        enumMap[2].Should().Be("down");
        enumMap[3].Should().Be("testing");
    }

    [Fact]
    public void Bind_WithCompleteValidJson_PopulatesOtlpProperties()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Otlp:Endpoint"] = "http://collector:4317",
            ["Otlp:ServiceName"] = "test-service",
        });

        var options = new OtlpOptions { Endpoint = "", ServiceName = "" };
        config.GetSection(OtlpOptions.SectionName).Bind(options);

        options.Endpoint.Should().Be("http://collector:4317");
        options.ServiceName.Should().Be("test-service");
    }

    [Fact]
    public void Bind_WithCompleteValidJson_PopulatesHeartbeatJobProperties()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["HeartbeatJob:IntervalSeconds"] = "25",
        });

        var options = new HeartbeatJobOptions();
        config.GetSection(HeartbeatJobOptions.SectionName).Bind(options);

        options.IntervalSeconds.Should().Be(25);
    }

    [Fact]
    public void Bind_WithCompleteValidJson_PopulatesCorrelationJobProperties()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CorrelationJob:IntervalSeconds"] = "45",
        });

        var options = new CorrelationJobOptions();
        config.GetSection(CorrelationJobOptions.SectionName).Bind(options);

        options.IntervalSeconds.Should().Be(45);
    }

    [Fact]
    public void Bind_WithCompleteValidJson_PopulatesLivenessProperties()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Liveness:GraceMultiplier"] = "3.5",
        });

        var options = new LivenessOptions();
        config.GetSection(LivenessOptions.SectionName).Bind(options);

        options.GraceMultiplier.Should().Be(3.5);
    }

    [Fact]
    public void Bind_WithCompleteValidJson_PopulatesChannelsProperties()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Channels:BoundedCapacity"] = "500",
        });

        var options = new ChannelsOptions();
        config.GetSection(ChannelsOptions.SectionName).Bind(options);

        options.BoundedCapacity.Should().Be(500);
    }

    [Fact]
    public void Bind_WithCompleteValidJson_PopulatesLoggingProperties()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Logging:EnableConsole"] = "true",
        });

        var options = new LoggingOptions();
        config.GetSection(LoggingOptions.SectionName).Bind(options);

        options.EnableConsole.Should().BeTrue();
    }
}
