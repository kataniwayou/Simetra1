using Simetra.Models;

namespace Simetra.Devices;

/// <summary>
/// Data contract for a device module that provides device identity and poll definitions
/// via code rather than configuration. Implementations are registered in DI and discovered
/// automatically by <c>DeviceRegistry</c> and <c>DeviceChannelManager</c> at startup.
/// All poll definitions returned by a module must have <c>Source = MetricPollSource.Module</c>.
/// </summary>
public interface IDeviceModule
{
    /// <summary>
    /// Device type identifier (e.g., "simetra", "router").
    /// </summary>
    string DeviceType { get; }

    /// <summary>
    /// Human-readable device name (e.g., "simetra-heartbeat").
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    /// IPv4 address string of the device (e.g., "127.0.0.1").
    /// </summary>
    string IpAddress { get; }

    /// <summary>
    /// Trap definitions for this device, used for OID matching on incoming traps.
    /// All entries must have <c>Source = MetricPollSource.Module</c>.
    /// </summary>
    IReadOnlyList<PollDefinitionDto> TrapDefinitions { get; }

    /// <summary>
    /// State poll definitions for this device, used for periodic SNMP polling.
    /// All entries must have <c>Source = MetricPollSource.Module</c>.
    /// </summary>
    IReadOnlyList<PollDefinitionDto> StatePollDefinitions { get; }
}
