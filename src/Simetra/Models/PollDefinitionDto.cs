using System.Collections.ObjectModel;
using Simetra.Configuration;

namespace Simetra.Models;

/// <summary>
/// Immutable runtime representation of a metric poll definition.
/// Created from <see cref="MetricPollOptions"/> via <see cref="FromOptions"/>; used by the
/// extraction pipeline for both trap and poll processing.
/// </summary>
/// <param name="MetricName">Name of the metric to emit (e.g., "simetra_cpu").</param>
/// <param name="MetricType">Type of metric (Gauge or Counter).</param>
/// <param name="Oids">Ordered list of OID entries to poll for this metric.</param>
/// <param name="IntervalSeconds">Polling interval in seconds.</param>
/// <param name="Source">Origin of this poll definition (Configuration or Module).</param>
public sealed record PollDefinitionDto(
    string MetricName,
    MetricType MetricType,
    IReadOnlyList<OidEntryDto> Oids,
    int IntervalSeconds,
    MetricPollSource Source)
{
    /// <summary>
    /// Converts a mutable <see cref="MetricPollOptions"/> configuration object into an
    /// immutable <see cref="PollDefinitionDto"/> for runtime use. The Source field is
    /// preserved from <paramref name="options"/> (already stamped by PostConfigure).
    /// </summary>
    /// <param name="options">The mutable configuration options to convert.</param>
    /// <returns>An immutable poll definition DTO.</returns>
    public static PollDefinitionDto FromOptions(MetricPollOptions options)
    {
        var oids = options.Oids
            .Select(o => new OidEntryDto(
                o.Oid,
                o.PropertyName,
                o.Role,
                o.EnumMap?.ToDictionary(kv => kv.Key, kv => kv.Value).AsReadOnly()))
            .ToList()
            .AsReadOnly();

        return new PollDefinitionDto(
            options.MetricName,
            options.MetricType,
            oids,
            options.IntervalSeconds,
            options.Source);
    }
}
