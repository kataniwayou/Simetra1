using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simetra.Configuration;
using Simetra.Devices;

namespace Simetra.Pipeline;

/// <summary>
/// Singleton that creates and manages one bounded <see cref="Channel{TrapEnvelope}"/>
/// per device (from both configuration and code-defined modules). Channels use
/// <see cref="BoundedChannelFullMode.DropOldest"/> with a Debug-level log callback
/// when items are dropped.
/// </summary>
public sealed class DeviceChannelManager : IDeviceChannelManager
{
    private readonly Dictionary<string, Channel<TrapEnvelope>> _channels;

    /// <summary>
    /// Initializes a new instance, creating one bounded channel per device from
    /// configuration and code-defined device modules.
    /// </summary>
    /// <param name="devicesOptions">Device configurations defining which channels to create.</param>
    /// <param name="channelsOptions">Channel capacity configuration.</param>
    /// <param name="modules">Code-defined device modules discovered via DI.</param>
    /// <param name="logger">Logger for item-dropped callbacks.</param>
    public DeviceChannelManager(
        IOptions<DevicesOptions> devicesOptions,
        IOptions<ChannelsOptions> channelsOptions,
        IEnumerable<IDeviceModule> modules,
        ILogger<DeviceChannelManager> logger)
    {
        var capacity = channelsOptions.Value.BoundedCapacity;
        var configDeviceNames = devicesOptions.Value.Devices.Select(d => d.Name);
        var moduleDeviceNames = modules.Select(m => m.DeviceName);
        var allDeviceNames = configDeviceNames.Concat(moduleDeviceNames);
        _channels = new Dictionary<string, Channel<TrapEnvelope>>(StringComparer.Ordinal);

        foreach (var deviceName in allDeviceNames)
        {
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true,
                AllowSynchronousContinuations = false
            };

            var name = deviceName;
            var channel = Channel.CreateBounded(options, (TrapEnvelope dropped) =>
            {
                logger.LogDebug(
                    "Trap dropped from device {DeviceName} channel (capacity {Capacity}), correlationId: {CorrelationId}",
                    name,
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
