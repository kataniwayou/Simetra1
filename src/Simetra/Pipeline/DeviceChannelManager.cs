using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simetra.Configuration;

namespace Simetra.Pipeline;

/// <summary>
/// Singleton that creates and manages one bounded <see cref="Channel{TrapEnvelope}"/>
/// per configured device. Channels use <see cref="BoundedChannelFullMode.DropOldest"/>
/// with a Debug-level log callback when items are dropped.
/// </summary>
public sealed class DeviceChannelManager : IDeviceChannelManager
{
    private readonly Dictionary<string, Channel<TrapEnvelope>> _channels;

    /// <summary>
    /// Initializes a new instance, creating one bounded channel per device from configuration.
    /// </summary>
    /// <param name="devicesOptions">Device configurations defining which channels to create.</param>
    /// <param name="channelsOptions">Channel capacity configuration.</param>
    /// <param name="logger">Logger for item-dropped callbacks.</param>
    public DeviceChannelManager(
        IOptions<DevicesOptions> devicesOptions,
        IOptions<ChannelsOptions> channelsOptions,
        ILogger<DeviceChannelManager> logger)
    {
        var devices = devicesOptions.Value.Devices;
        var capacity = channelsOptions.Value.BoundedCapacity;
        _channels = new Dictionary<string, Channel<TrapEnvelope>>(
            devices.Count, StringComparer.Ordinal);

        foreach (var device in devices)
        {
            var deviceName = device.Name;
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true,
                AllowSynchronousContinuations = false
            };

            var channel = Channel.CreateBounded(options, (TrapEnvelope dropped) =>
            {
                logger.LogDebug(
                    "Trap dropped from device {DeviceName} channel (capacity {Capacity}), correlationId: {CorrelationId}",
                    deviceName,
                    capacity,
                    dropped.CorrelationId);
            });

            _channels[deviceName] = channel;
        }
    }

    /// <inheritdoc />
    public ChannelWriter<TrapEnvelope> GetWriter(string deviceName)
    {
        return _channels[deviceName].Writer;
    }

    /// <inheritdoc />
    public ChannelReader<TrapEnvelope> GetReader(string deviceName)
    {
        return _channels[deviceName].Reader;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DeviceNames => _channels.Keys;
}
