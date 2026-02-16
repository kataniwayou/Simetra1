using Simetra.Models;

namespace Simetra.Devices;

/// <summary>
/// Type-level data contract for a device module that provides trap and poll definitions
/// for all devices of a given <see cref="DeviceType"/>. Implementations are registered
/// in DI and discovered automatically by <c>DeviceRegistry</c>, <c>PollDefinitionRegistry</c>,
/// and scheduling at startup. Device identity (Name, IP) comes from configuration —
/// the module provides only type-level OID definitions.
/// All poll definitions returned by a module must have <c>Source = MetricPollSource.Module</c>.
/// </summary>
public interface IDeviceModule
{
    /// <summary>
    /// Device type identifier (e.g., "simetra", "NPB", "OBP"). Matched against
    /// <see cref="Configuration.DeviceOptions.DeviceType"/> to apply module definitions
    /// to all config devices of this type.
    /// </summary>
    string DeviceType { get; }

    /// <summary>
    /// Trap definitions for this device type, used for OID matching on incoming traps.
    /// Applied to every config device whose DeviceType matches this module.
    /// All entries must have <c>Source = MetricPollSource.Module</c>.
    /// </summary>
    IReadOnlyList<PollDefinitionDto> TrapDefinitions { get; }

    /// <summary>
    /// State poll definitions for this device type, used for periodic SNMP polling.
    /// Applied to every config device whose DeviceType matches this module.
    /// All entries must have <c>Source = MetricPollSource.Module</c>.
    /// </summary>
    IReadOnlyList<PollDefinitionDto> StatePollDefinitions { get; }
}
