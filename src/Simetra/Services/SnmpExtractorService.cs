using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
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
        throw new NotImplementedException();
    }
}
