using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Simetra.Pipeline;

/// <summary>
/// Provides O(1) device lookup by IP address for incoming SNMP traps.
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>
    /// Attempts to find a registered device by its sender IP address.
    /// The IP is normalized to IPv4 before lookup.
    /// </summary>
    /// <param name="senderIp">The IP address of the trap sender.</param>
    /// <param name="device">The device info if found; null otherwise.</param>
    /// <returns>True if the device was found; false otherwise.</returns>
    bool TryGetDevice(IPAddress senderIp, [NotNullWhen(true)] out DeviceInfo? device);
}
