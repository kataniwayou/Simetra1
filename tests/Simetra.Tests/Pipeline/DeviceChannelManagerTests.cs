using System.Net;
using System.Threading.Channels;
using FluentAssertions;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Simetra.Configuration;
using Simetra.Pipeline;

namespace Simetra.Tests.Pipeline;

public class DeviceChannelManagerTests
{
    private static DevicesOptions MakeDevicesOptions(params string[] deviceNames)
    {
        var devices = deviceNames
            .Select(name => new DeviceOptions
            {
                Name = name,
                IpAddress = "10.0.0.1",
                DeviceType = "router",
                MetricPolls = []
            })
            .ToList();

        return new DevicesOptions { Devices = devices };
    }

    private static TrapEnvelope MakeEnvelope(string correlationId)
    {
        return new TrapEnvelope
        {
            Varbinds = new List<Variable>(),
            SenderAddress = IPAddress.Loopback,
            ReceivedAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId
        };
    }

    private static DeviceChannelManager CreateManager(
        DevicesOptions devicesOptions,
        int boundedCapacity = 100)
    {
        var channelsOptions = new ChannelsOptions { BoundedCapacity = boundedCapacity };
        return new DeviceChannelManager(
            Options.Create(devicesOptions),
            Options.Create(channelsOptions),
            Mock.Of<ILogger<DeviceChannelManager>>());
    }

    [Fact]
    public void GetWriter_KnownDevice_ReturnsWriter()
    {
        var manager = CreateManager(MakeDevicesOptions("device-1"));

        var act = () => manager.GetWriter("device-1");

        act.Should().NotThrow();
    }

    [Fact]
    public void GetReader_KnownDevice_ReturnsReader()
    {
        var manager = CreateManager(MakeDevicesOptions("device-1"));

        var act = () => manager.GetReader("device-1");

        act.Should().NotThrow();
    }

    [Fact]
    public void GetWriter_UnknownDevice_ThrowsKeyNotFound()
    {
        var manager = CreateManager(MakeDevicesOptions("device-1"));

        var act = () => manager.GetWriter("unknown-device");

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void GetReader_UnknownDevice_ThrowsKeyNotFound()
    {
        var manager = CreateManager(MakeDevicesOptions("device-1"));

        var act = () => manager.GetReader("unknown-device");

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void DeviceNames_ReturnsAllRegistered()
    {
        var manager = CreateManager(MakeDevicesOptions("device-a", "device-b", "device-c"));

        manager.DeviceNames.Should().BeEquivalentTo(["device-a", "device-b", "device-c"]);
    }

    [Fact]
    public async Task WriteToFullChannel_DropsOldestItem()
    {
        // Capacity 2 with DropOldest
        var manager = CreateManager(MakeDevicesOptions("device-1"), boundedCapacity: 2);
        var writer = manager.GetWriter("device-1");
        var reader = manager.GetReader("device-1");

        var env1 = MakeEnvelope("corr-1");
        var env2 = MakeEnvelope("corr-2");
        var env3 = MakeEnvelope("corr-3");

        // Fill to capacity
        await writer.WriteAsync(env1);
        await writer.WriteAsync(env2);

        // Write 3rd -- should drop env1 (oldest)
        await writer.WriteAsync(env3);

        // Read 2 items: should be env2 and env3 (env1 was dropped)
        var first = await reader.ReadAsync();
        var second = await reader.ReadAsync();

        first.CorrelationId.Should().Be("corr-2");
        second.CorrelationId.Should().Be("corr-3");
    }

    [Fact]
    public async Task CompleteAll_PreventsSubsequentWrites()
    {
        var manager = CreateManager(MakeDevicesOptions("device-1"));
        var writer = manager.GetWriter("device-1");

        manager.CompleteAll();

        var act = async () => await writer.WriteAsync(MakeEnvelope("corr-x"));

        await act.Should().ThrowAsync<ChannelClosedException>();
    }

}
