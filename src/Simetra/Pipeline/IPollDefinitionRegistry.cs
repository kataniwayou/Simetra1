using System.Diagnostics.CodeAnalysis;
using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Provides O(1) lookup for poll definitions by (deviceName, metricName) composite key.
/// Poll jobs receive device name and metric name from their Quartz JobDataMap and use
/// this registry to resolve the full <see cref="PollDefinitionDto"/> at execution time.
/// </summary>
public interface IPollDefinitionRegistry
{
    /// <summary>
    /// Attempts to find a poll definition by device name and metric name.
    /// Lookup is case-insensitive on both keys.
    /// </summary>
    /// <param name="deviceName">The device name from JobDataMap.</param>
    /// <param name="metricName">The metric name from JobDataMap.</param>
    /// <param name="definition">The poll definition if found; null otherwise.</param>
    /// <returns>True if the definition was found; false otherwise.</returns>
    bool TryGetDefinition(string deviceName, string metricName, [NotNullWhen(true)] out PollDefinitionDto? definition);

    /// <summary>
    /// Returns all state poll definitions (Source=Module) as (deviceName, definition) tuples.
    /// Used for dynamic Quartz job registration at startup.
    /// </summary>
    IReadOnlyList<(string DeviceName, PollDefinitionDto Definition)> GetAllStatePollDefinitions();

    /// <summary>
    /// Returns all metric poll definitions (Source=Configuration) as (deviceName, definition) tuples.
    /// Used for dynamic Quartz job registration at startup.
    /// </summary>
    IReadOnlyList<(string DeviceName, PollDefinitionDto Definition)> GetAllMetricPollDefinitions();
}
