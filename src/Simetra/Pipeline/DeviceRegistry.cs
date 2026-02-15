using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Options;
using Simetra.Configuration;
using Simetra.Devices;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Singleton registry that maps normalized IPv4 addresses to <see cref="DeviceInfo"/>
/// for O(1) device lookup. Built once at startup from <see cref="DevicesOptions"/>
/// and any registered <see cref="IDeviceModule"/> implementations.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly Dictionary<IPAddress, DeviceInfo> _devices;

    /// <summary>
    /// Initializes the registry by building an IP-to-device dictionary from configuration
    /// and code-defined device modules. Config devices are registered first; module devices
    /// are registered second so they take precedence on IP collision.
    /// Each device's IP is normalized to IPv4 via <see cref="IPAddress.MapToIPv4"/>.
    /// MetricPolls are converted to <see cref="PollDefinitionDto"/> via
    /// <see cref="PollDefinitionDto.FromOptions"/>.
    /// </summary>
    /// <param name="devicesOptions">The configured devices to register.</param>
    /// <param name="modules">Code-defined device modules discovered via DI.</param>
    public DeviceRegistry(
        IOptions<DevicesOptions> devicesOptions,
        IEnumerable<IDeviceModule> modules)
    {
        var devices = devicesOptions.Value.Devices;
        _devices = new Dictionary<IPAddress, DeviceInfo>(devices.Count);

        foreach (var d in devices)
        {
            var ip = IPAddress.Parse(d.IpAddress).MapToIPv4();
            var trapDefinitions = d.MetricPolls
                .Select(PollDefinitionDto.FromOptions)
                .ToList()
                .AsReadOnly();

            var info = new DeviceInfo(d.Name, d.IpAddress, d.DeviceType, trapDefinitions);
            _devices[ip] = info;
        }

        foreach (var module in modules)
        {
            var ip = IPAddress.Parse(module.IpAddress).MapToIPv4();
            var info = new DeviceInfo(module.DeviceName, module.IpAddress, module.DeviceType, module.TrapDefinitions);
            _devices[ip] = info;
        }
    }

    /// <inheritdoc />
    public bool TryGetDevice(IPAddress senderIp, [NotNullWhen(true)] out DeviceInfo? device)
    {
        return _devices.TryGetValue(senderIp.MapToIPv4(), out device);
    }
}
