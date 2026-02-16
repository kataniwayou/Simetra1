using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Simetra.Configuration;
using Simetra.Devices;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Singleton registry that indexes all poll definitions by a composite
/// <c>"deviceName::metricName"</c> key for O(1) lookup. Built once at startup from
/// device modules (state polls) and configuration (metric polls).
/// </summary>
public sealed class PollDefinitionRegistry : IPollDefinitionRegistry
{
    private readonly Dictionary<string, PollDefinitionDto> _definitions;
    private readonly List<(string DeviceName, PollDefinitionDto Definition)> _statePollDefinitions;
    private readonly List<(string DeviceName, PollDefinitionDto Definition)> _metricPollDefinitions;

    /// <summary>
    /// Initializes the registry by indexing all poll definitions from modules and configuration.
    /// Module <see cref="IDeviceModule.StatePollDefinitions"/> are applied to every config device
    /// whose <see cref="DeviceOptions.DeviceType"/> matches the module's <see cref="IDeviceModule.DeviceType"/>.
    /// Configuration <see cref="DeviceOptions.MetricPolls"/> become metric poll entries.
    /// </summary>
    /// <param name="devicesOptions">The configured devices providing metric poll definitions.</param>
    /// <param name="modules">Code-defined device modules providing type-level state poll definitions.</param>
    public PollDefinitionRegistry(
        IOptions<DevicesOptions> devicesOptions,
        IEnumerable<IDeviceModule> modules)
    {
        _definitions = new Dictionary<string, PollDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        _statePollDefinitions = new List<(string, PollDefinitionDto)>();
        _metricPollDefinitions = new List<(string, PollDefinitionDto)>();

        // Index modules by DeviceType for O(1) lookup
        var modulesByType = modules.ToDictionary(m => m.DeviceType, StringComparer.OrdinalIgnoreCase);

        // Apply module state poll definitions to each config device by DeviceType
        foreach (var device in devicesOptions.Value.Devices)
        {
            if (modulesByType.TryGetValue(device.DeviceType, out var module))
            {
                foreach (var def in module.StatePollDefinitions)
                {
                    var key = $"{device.Name}::{def.MetricName}";
                    _definitions[key] = def;
                    _statePollDefinitions.Add((device.Name, def));
                }
            }
        }

        // Index metric poll definitions from configuration (Source=Configuration)
        foreach (var device in devicesOptions.Value.Devices)
        {
            foreach (var poll in device.MetricPolls)
            {
                var def = PollDefinitionDto.FromOptions(poll);
                var key = $"{device.Name}::{def.MetricName}";
                _definitions[key] = def;
                _metricPollDefinitions.Add((device.Name, def));
            }
        }
    }

    /// <inheritdoc />
    public bool TryGetDefinition(string deviceName, string metricName, [NotNullWhen(true)] out PollDefinitionDto? definition)
    {
        var key = $"{deviceName}::{metricName}";
        return _definitions.TryGetValue(key, out definition);
    }

    /// <inheritdoc />
    public IReadOnlyList<(string DeviceName, PollDefinitionDto Definition)> GetAllStatePollDefinitions()
    {
        return _statePollDefinitions.AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<(string DeviceName, PollDefinitionDto Definition)> GetAllMetricPollDefinitions()
    {
        return _metricPollDefinitions.AsReadOnly();
    }
}
