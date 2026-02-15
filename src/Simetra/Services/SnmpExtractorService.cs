using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
using Simetra.Configuration;
using Simetra.Models;
using Simetra.Pipeline;

namespace Simetra.Services;

/// <summary>
/// Generic SNMP varbind extractor. Pattern-matches each varbind's ISnmpData type
/// and produces an <see cref="ExtractionResult"/> with metrics, labels, and enum-map metadata.
/// Same logic for traps and polls -- no per-device-type branching.
/// </summary>
public sealed class SnmpExtractorService : ISnmpExtractor
{
    private readonly ILogger<SnmpExtractorService> _logger;

    public SnmpExtractorService(ILogger<SnmpExtractorService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ExtractionResult Extract(IList<Variable> varbinds, PollDefinitionDto definition)
    {
        var oidLookup = definition.Oids.ToDictionary(o => o.Oid, o => o);

        var metrics = new Dictionary<string, long>();
        var labels = new Dictionary<string, string>();
        var enumMetadata = new Dictionary<string, IReadOnlyDictionary<int, string>>();

        foreach (var varbind in varbinds)
        {
            var oidString = varbind.Id.ToString();

            if (!oidLookup.TryGetValue(oidString, out var entry))
            {
                _logger.LogDebug(
                    "Varbind OID {Oid} not found in definition {MetricName}, skipping",
                    oidString, definition.MetricName);
                continue;
            }

            switch (entry.Role)
            {
                case OidRole.Metric:
                    var numericValue = ExtractNumericValue(varbind.Data);
                    if (numericValue.HasValue)
                    {
                        metrics[entry.PropertyName] = numericValue.Value;

                        if (entry.EnumMap is { Count: > 0 })
                        {
                            enumMetadata[entry.PropertyName] = entry.EnumMap;
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Non-numeric SNMP data {TypeCode} for Metric role OID {Oid}, skipping",
                            varbind.Data.TypeCode, oidString);
                    }
                    break;

                case OidRole.Label:
                    var labelValue = ExtractLabelValue(varbind.Data, entry.EnumMap);
                    if (labelValue is not null)
                    {
                        labels[entry.PropertyName] = labelValue;
                    }
                    break;
            }
        }

        return new ExtractionResult
        {
            Definition = definition,
            Metrics = metrics,
            Labels = labels,
            EnumMapMetadata = enumMetadata
        };
    }

    private static long? ExtractNumericValue(ISnmpData data)
    {
        return data switch
        {
            Integer32 i => i.ToInt32(),
            Counter32 c32 => (long)c32.ToUInt32(),
            Counter64 c64 => (long)c64.ToUInt64(),
            Gauge32 g => (long)g.ToUInt32(),
            TimeTicks tt => (long)tt.ToUInt32(),
            _ => null
        };
    }

    private static string? ExtractLabelValue(ISnmpData data, IReadOnlyDictionary<int, string>? enumMap)
    {
        if (enumMap is not null && data is Integer32 i32)
        {
            return enumMap.TryGetValue(i32.ToInt32(), out var mapped)
                ? mapped
                : i32.ToInt32().ToString();
        }

        return data switch
        {
            OctetString os => os.ToString(),
            IP ip => ip.ToString(),
            Integer32 i => i.ToInt32().ToString(),
            _ => data.ToString()
        };
    }
}
