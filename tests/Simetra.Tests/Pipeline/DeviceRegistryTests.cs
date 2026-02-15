using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Simetra.Configuration;
using Simetra.Devices;
using Simetra.Models;
using Simetra.Pipeline;

namespace Simetra.Tests.Pipeline;

public class DeviceRegistryTests
{
    private static DevicesOptions MakeDevicesOptions(params DeviceOptions[] devices)
        => new() { Devices = devices.ToList() };

    private static DeviceOptions MakeDeviceOptions(string name, string ip, string type = "router")
        => new()
        {
            Name = name,
            IpAddress = ip,
            DeviceType = type,
            MetricPolls = []
        };

    private static DeviceRegistry CreateRegistry(
        DevicesOptions devicesOptions,
        params IDeviceModule[] modules)
    {
        return new DeviceRegistry(
            Options.Create(devicesOptions),
            modules.AsEnumerable());
    }

    [Fact]
    public void TryGetDevice_KnownIp_ReturnsTrue()
    {
        var options = MakeDevicesOptions(MakeDeviceOptions("router-1", "10.0.0.1"));
        var registry = CreateRegistry(options);

        var found = registry.TryGetDevice(IPAddress.Parse("10.0.0.1"), out var device);

        found.Should().BeTrue();
        device!.Name.Should().Be("router-1");
    }

    [Fact]
    public void TryGetDevice_UnknownIp_ReturnsFalse()
    {
        var options = MakeDevicesOptions(MakeDeviceOptions("router-1", "10.0.0.1"));
        var registry = CreateRegistry(options);

        var found = registry.TryGetDevice(IPAddress.Parse("10.0.0.99"), out var device);

        found.Should().BeFalse();
        device.Should().BeNull();
    }

    [Fact]
    public void TryGetDevice_NormalizesIpv6MappedToIpv4()
    {
        var options = MakeDevicesOptions(MakeDeviceOptions("router-1", "10.0.0.1"));
        var registry = CreateRegistry(options);

        // IPv6-mapped IPv4 address ::ffff:10.0.0.1 should resolve to same device
        var ipv6Mapped = IPAddress.Parse("::ffff:10.0.0.1");
        var found = registry.TryGetDevice(ipv6Mapped, out var device);

        found.Should().BeTrue();
        device!.Name.Should().Be("router-1");
    }

    [Fact]
    public void TryGetDeviceByName_CaseInsensitive()
    {
        var options = MakeDevicesOptions(MakeDeviceOptions("Router-1", "10.0.0.1"));
        var registry = CreateRegistry(options);

        var found = registry.TryGetDeviceByName("router-1", out var device);

        found.Should().BeTrue();
        device!.Name.Should().Be("Router-1");
    }

    [Fact]
    public void TryGetDeviceByName_UnknownName_ReturnsFalse()
    {
        var options = MakeDevicesOptions(MakeDeviceOptions("router-1", "10.0.0.1"));
        var registry = CreateRegistry(options);

        var found = registry.TryGetDeviceByName("switch-99", out var device);

        found.Should().BeFalse();
        device.Should().BeNull();
    }

    [Fact]
    public void ModuleDevices_OverrideConfigOnIpCollision()
    {
        var options = MakeDevicesOptions(MakeDeviceOptions("config-device", "10.0.0.1"));

        var moduleTrapDefs = new List<PollDefinitionDto>().AsReadOnly();
        var moduleStateDefs = new List<PollDefinitionDto>().AsReadOnly();

        var moduleMock = new Mock<IDeviceModule>();
        moduleMock.Setup(m => m.DeviceName).Returns("module-device");
        moduleMock.Setup(m => m.IpAddress).Returns("10.0.0.1");
        moduleMock.Setup(m => m.DeviceType).Returns("simetra");
        moduleMock.Setup(m => m.TrapDefinitions).Returns(moduleTrapDefs);
        moduleMock.Setup(m => m.StatePollDefinitions).Returns(moduleStateDefs);

        var registry = CreateRegistry(options, moduleMock.Object);

        var found = registry.TryGetDevice(IPAddress.Parse("10.0.0.1"), out var device);

        found.Should().BeTrue();
        device!.Name.Should().Be("module-device", "module devices override config on IP collision");
    }
}
