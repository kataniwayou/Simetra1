# Phase 2: Domain Models + Extraction Engine - Research

**Researched:** 2026-02-15
**Domain:** SNMP varbind extraction, domain modeling, SharpSnmpLib type system, runtime DTOs from configuration Options
**Confidence:** HIGH

## Summary

Phase 2 builds two things on top of the Phase 1 configuration layer: (1) runtime domain models (PollDefinitionDto and OidEntryDto that wrap/transform the existing config Options classes into immutable runtime objects), and (2) a generic extraction engine that takes SNMP varbinds + a PollDefinitionDto and produces an ExtractionResult containing metric values, label values, and enum-map metadata.

The core library is Lextm.SharpSnmpLib 12.5.7, which provides the SNMP type system. Its `Variable` class holds an `ObjectIdentifier` (the OID) and `ISnmpData` (the typed value). The extractor must pattern-match `ISnmpData` against seven concrete types (`Integer32`, `OctetString`, `Counter32`, `Counter64`, `Gauge32`, `TimeTicks`, `IP`) and extract numeric or string values. Each type has a specific extraction method: `Integer32.ToInt32()`, `Counter32.ToUInt32()`, `Counter64.ToUInt64()`, `Gauge32.ToUInt32()`, `TimeTicks.ToUInt32()`, `OctetString.ToString()`, and `IP.ToString()`.

The key architectural decision is the relationship between config Options (mutable, bound from JSON) and runtime DTOs (immutable, used by the pipeline). PollDefinitionDto is NOT the same class as MetricPollOptions -- it is a separate immutable record/class in the `Simetra.Models` namespace that is constructed FROM MetricPollOptions (for config-loaded polls) or constructed directly in device module code (for Source=Module polls). This separation keeps the configuration layer clean and gives the pipeline immutable, validated objects.

**Primary recommendation:** Create PollDefinitionDto and OidEntryDto as immutable C# records in `Simetra.Models`. Build the extractor as `ISnmpExtractor` / `SnmpExtractorService` in `Simetra.Services` using C# pattern matching on `ISnmpData.TypeCode` (or type-test patterns) to handle all seven SNMP types. The extraction result should be a flat `ExtractionResult` class containing dictionaries for metrics (PropertyName -> numeric value), labels (PropertyName -> string value), and enum-map metadata (PropertyName -> Dictionary<int, string>).

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Lextm.SharpSnmpLib | 12.5.7 | SNMP protocol types (Variable, ObjectIdentifier, ISnmpData, Integer32, OctetString, Counter32, Counter64, Gauge32, TimeTicks, IP) | Only maintained .NET SNMP library with full v1/v2c/v3 support. MIT license, 4.4M NuGet downloads, targets net8.0 (compatible with net9.0). No dependencies. |

**Confidence:** HIGH -- verified via [NuGet package page](https://www.nuget.org/packages/Lextm.SharpSnmpLib/) (version 12.5.7, released 2025-11-03, targets net8.0 + net4.7.1) and [GitHub source](https://github.com/lextudio/sharpsnmplib) (active repository).

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| (none needed) | - | - | Phase 2 uses only SharpSnmpLib types + built-in .NET. No additional packages required beyond the test stack from Phase 1. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Lextm.SharpSnmpLib 12.5.7 | SnmpSharpNet | SnmpSharpNet is less maintained, fewer downloads, different API. SharpSnmpLib is already the project decision. |
| C# records for DTOs | Regular sealed classes | Records provide value equality, `with` expressions, concise syntax. Records are the modern .NET approach for immutable data carriers. No downside for this use case. |
| Pattern matching on ISnmpData | Switch on SnmpType enum | Pattern matching (`data is Integer32 i32`) is more idiomatic C# and provides the typed variable in one step. Both work; pattern matching is cleaner. |

### Installation

```xml
<!-- Add to Simetra.csproj -->
<ItemGroup>
  <PackageReference Include="Lextm.SharpSnmpLib" Version="12.5.7" />
</ItemGroup>
```

## Architecture Patterns

### Recommended Project Structure
```
src/Simetra/
├── Models/                          # Domain models (Phase 2 additions)
│   ├── PollDefinitionDto.cs         # Immutable runtime poll definition
│   ├── OidEntryDto.cs               # Immutable OID entry within a poll definition
│   └── ExtractionResult.cs          # Output of the generic extractor
│
├── Services/                        # Service layer (Phase 2 additions)
│   └── SnmpExtractorService.cs      # Generic extractor implementation
│
├── Pipeline/                        # Pipeline contracts (Phase 2 additions)
│   └── ISnmpExtractor.cs            # Extractor interface
│
└── Configuration/                   # Already exists from Phase 1
    ├── MetricPollOptions.cs         # Config binding (mutable, JSON-bound)
    ├── OidEntryOptions.cs           # Config binding (mutable, JSON-bound)
    ├── MetricType.cs                # Shared enum (Gauge, Counter)
    ├── OidRole.cs                   # Shared enum (Metric, Label)
    └── MetricPollSource.cs          # Shared enum (Configuration, Module)
```

### Pattern 1: Config Options vs. Runtime DTOs (Two-Layer Model)

**What:** Configuration Options classes (MetricPollOptions, OidEntryOptions) are mutable binding targets for `IOptions<T>`. Runtime DTOs (PollDefinitionDto, OidEntryDto) are immutable objects used by the pipeline. A factory/mapping method converts Options -> DTOs at load time.

**When to use:** Always -- this is the established .NET pattern for separating config binding from runtime usage.

**Why:**
- Config Options must be mutable for the IOptions binding system to populate them
- Pipeline components need immutable, validated data to avoid mutation bugs
- Source field is stamped at conversion time (Module vs Configuration)
- Device modules create PollDefinitionDto directly (no Options intermediary)

**Example:**
```csharp
// Runtime DTO -- immutable record in Simetra.Models
namespace Simetra.Models;

public sealed record OidEntryDto(
    string Oid,
    string PropertyName,
    OidRole Role,
    IReadOnlyDictionary<int, string>? EnumMap);

public sealed record PollDefinitionDto(
    string MetricName,
    MetricType MetricType,
    IReadOnlyList<OidEntryDto> Oids,
    int IntervalSeconds,
    MetricPollSource Source);

// Conversion from config Options to runtime DTO
public static PollDefinitionDto FromOptions(MetricPollOptions options)
{
    return new PollDefinitionDto(
        MetricName: options.MetricName,
        MetricType: options.MetricType,
        Oids: options.Oids.Select(o => new OidEntryDto(
            o.Oid, o.PropertyName, o.Role,
            o.EnumMap?.AsReadOnly())).ToList().AsReadOnly(),
        IntervalSeconds: options.IntervalSeconds,
        Source: options.Source);  // Already stamped by PostConfigure
}
```

### Pattern 2: Generic Extraction via ISnmpData Pattern Matching

**What:** The extractor receives a list of SharpSnmpLib `Variable` objects (varbinds) and a `PollDefinitionDto`. For each varbind, it finds the matching OidEntryDto by OID string, then pattern-matches the `ISnmpData` to extract a typed value.

**When to use:** For all extraction -- traps and polls use the same logic.

**Example:**
```csharp
// Source: SharpSnmpLib GitHub source analysis
// Namespace: Lextm.SharpSnmpLib
// Variable.Id -> ObjectIdentifier (use .ToString() for dotted string)
// Variable.Data -> ISnmpData (pattern match to concrete type)

private static object ExtractValue(ISnmpData data)
{
    return data switch
    {
        Integer32 i    => (object)i.ToInt32(),
        Counter32 c32  => (object)(long)c32.ToUInt32(),
        Counter64 c64  => (object)(long)c64.ToUInt64(),
        Gauge32 g      => (object)(long)g.ToUInt32(),
        TimeTicks tt   => (object)(long)tt.ToUInt32(),
        OctetString os => (object)os.ToString(),
        IP ip          => (object)ip.ToString(),
        _              => throw new NotSupportedException(
                              $"Unsupported SNMP type: {data.TypeCode}")
    };
}
```

### Pattern 3: ExtractionResult as Flat Data Container

**What:** The extraction result is a data container holding the extracted values categorized by role, plus metadata. It is NOT a strongly typed domain object yet -- the "strongly typed domain objects per device type" (EXTR-08) are created BY device modules in Phase 5 from the ExtractionResult, not by the generic extractor itself.

**When to use:** The extractor always produces an ExtractionResult. Phase 5 device modules convert ExtractionResult to typed domain objects (HeartbeatData, etc.).

**Why this matters:** EXTR-06 says "no per-device-type logic" in the extractor. EXTR-08 says "produces strongly typed domain objects per device type." These are reconciled by having the extractor produce a generic ExtractionResult, and device modules (Phase 5) provide a mapping function to convert it to their specific domain object type.

**Example:**
```csharp
namespace Simetra.Models;

/// <summary>
/// Result of extracting SNMP varbinds using a PollDefinitionDto.
/// Contains metric values (Role:Metric), label values (Role:Label),
/// and enum-map metadata for Grafana value mappings.
/// </summary>
public sealed class ExtractionResult
{
    /// <summary>
    /// The PollDefinitionDto that produced this result.
    /// </summary>
    public PollDefinitionDto Definition { get; init; } = null!;

    /// <summary>
    /// Metric values keyed by PropertyName.
    /// Values are raw SNMP numeric values (int, long, or ulong cast to long).
    /// EnumMap is NOT applied here -- raw integers preserved.
    /// </summary>
    public IReadOnlyDictionary<string, long> Metrics { get; init; }
        = new Dictionary<string, long>();

    /// <summary>
    /// Label values keyed by PropertyName.
    /// For Role:Label with EnumMap: the enum-mapped string.
    /// For Role:Label without EnumMap: the raw SNMP string value.
    /// </summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// EnumMap metadata for Role:Metric OIDs that have EnumMap defined.
    /// Stored for downstream use (Grafana value mappings), NOT used as metric values.
    /// Keyed by PropertyName -> { rawInt -> displayString }.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> EnumMapMetadata { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<int, string>>();
}
```

### Pattern 4: Numeric Value Normalization to long

**What:** All numeric SNMP values (Integer32, Counter32, Counter64, Gauge32, TimeTicks) are normalized to `long` for uniform handling in the extraction result. This avoids downstream code needing to handle `int`, `uint`, and `ulong` separately.

**Why:**
- `Integer32.ToInt32()` returns `int` (fits in `long`)
- `Counter32.ToUInt32()` returns `uint` (fits in `long`)
- `Gauge32.ToUInt32()` returns `uint` (fits in `long`)
- `TimeTicks.ToUInt32()` returns `uint` (fits in `long`)
- `Counter64.ToUInt64()` returns `ulong` -- values up to `long.MaxValue` fit; values above are extremely rare in practice (would require counter wrapping past 9.2 quintillion)

**Tradeoff:** Counter64 values exceeding `long.MaxValue` (9,223,372,036,854,775,807) would overflow. In practice, SNMP counters at this scale are reset or wrapped long before reaching this limit. For v1, normalizing to `long` is pragmatic and avoids `ulong` complications in downstream metric reporting (OpenTelemetry meters accept `long`, not `ulong`).

**When to use:** Always for metric values in Phase 2. If Counter64 overflow becomes a real concern, it can be addressed in a future phase.

### Anti-Patterns to Avoid

- **Mutating config Options in the pipeline:** Never use `MetricPollOptions` directly in the extractor or processing layers. Always convert to immutable `PollDefinitionDto` first. The config layer is for binding; the pipeline uses DTOs.

- **Per-device-type extraction logic:** The extractor must NOT contain `if (deviceType == "router")` branches. All extraction is driven by PollDefinitionDto.Oids entries. Device-specific behavior lives in device modules (Phase 5).

- **Using EnumMap values as metric values:** EXTR-04 is explicit: EnumMap is metadata only. The raw SNMP integer is always the metric value. EnumMap is stored in ExtractionResult.EnumMapMetadata for downstream consumers (Grafana value mappings).

- **Hardcoding SNMP OIDs in the extractor:** The extractor receives OIDs via PollDefinitionDto. It should never contain literal OID strings.

- **Casting ISnmpData to string via ToString() for numeric types:** Always use the typed extraction methods (`ToInt32()`, `ToUInt32()`, `ToUInt64()`). The `ToString()` on numeric types returns formatted strings, not raw values suitable for metrics.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SNMP type parsing | Custom byte-level SNMP decoders | SharpSnmpLib `Variable.Data` pattern matching | SharpSnmpLib already handles BER encoding, type identification, and value extraction. Reimplementing this is error-prone and unnecessary. |
| OID string comparison | Custom OID parsing/comparison | `ObjectIdentifier` equality or plain string comparison on normalized OIDs | SharpSnmpLib provides `ObjectIdentifier` with `Equals()` and `CompareTo()`. For lookup by OID string, a `Dictionary<string, OidEntryDto>` with the OID string as key is sufficient. |
| Immutable collections | Manual `ReadOnlyCollection` wrappers | `IReadOnlyList<T>` / `IReadOnlyDictionary<TK,TV>` with `.AsReadOnly()` | .NET built-in immutable interfaces are standard and well-understood. No need for `System.Collections.Immutable` here (no concurrent mutation). |
| Value type boxing avoidance | Custom union types for int/long/string | Simple `object` or normalize to `long` for metrics, `string` for labels | The extraction result has clear role-based separation (metrics are `long`, labels are `string`). No need for complex discriminated unions. |

**Key insight:** The extractor is fundamentally a mapping function: `(IList<Variable>, PollDefinitionDto) -> ExtractionResult`. SharpSnmpLib handles all SNMP protocol complexity. The extractor's job is just OID lookup + type extraction + role-based categorization. Keep it simple.

## Common Pitfalls

### Pitfall 1: OID Matching -- Exact vs. Prefix

**What goes wrong:** SNMP trap varbinds may contain OIDs that are child OIDs of the defined OID (e.g., defined OID is `1.3.6.1.2.1.2.2.1.7`, but trap varbind contains `1.3.6.1.2.1.2.2.1.7.1` with an instance suffix). Using exact string match would miss these.

**Why it happens:** SNMP table OIDs have instance identifiers appended (the `.1` suffix for interface index 1). Scalar OIDs end in `.0` and are exact matches.

**How to avoid:** Use exact OID string matching for the initial implementation. The design document shows OIDs with full paths including instance suffixes (e.g., `1.3.6.1.2.1.2.2.1.7.1` in varbind notation). PollDefinitionDto OIDs should include the full OID with instance identifiers. If prefix matching is needed later (for table walks), it can be added as an enhancement. The current requirements (EXTR-01 through EXTR-09) do not mention table walks or prefix matching.

**Warning signs:** Varbinds arriving with unmatched OIDs despite being expected, or extraction results missing properties.

### Pitfall 2: Counter32/Gauge32 unsigned values vs. signed conversion

**What goes wrong:** `Counter32.ToUInt32()` and `Gauge32.ToUInt32()` return `uint`. If naively cast to `int`, values above `int.MaxValue` (2,147,483,647) would overflow to negative numbers, producing incorrect metrics.

**Why it happens:** SNMP Counter32 and Gauge32 are unsigned 32-bit values (range 0 to 4,294,967,295). C# `int` is signed 32-bit (range -2B to +2B).

**How to avoid:** Cast `uint` to `long`, not `int`. `long` can represent the full `uint` range without loss. This is why the ExtractionResult uses `long` for all metric values.

**Warning signs:** Negative metric values appearing for counters or gauges that should be large positive numbers.

### Pitfall 3: OctetString encoding

**What goes wrong:** `OctetString.ToString()` uses the default encoding (which may not be UTF-8). Some SNMP devices send non-ASCII or binary data in OctetString fields.

**Why it happens:** SNMP OctetString is a raw byte sequence. SharpSnmpLib's `OctetString.ToString()` decodes using `OctetString.DefaultEncoding` (configurable, defaults to system encoding).

**How to avoid:** Use `OctetString.ToString()` for the default case (most SNMP string values are ASCII). If binary OctetStrings are encountered (e.g., MAC addresses), they can be handled via `OctetString.ToHexString()` or `OctetString.ToPhysicalAddress()`. For Phase 2, the default `ToString()` is sufficient -- binary OctetString handling can be added if needed for specific device modules.

**Warning signs:** Garbled or empty string values for labels that should contain readable text.

### Pitfall 4: Confusing Config Options with Runtime DTOs

**What goes wrong:** Using `MetricPollOptions` directly in the extractor, leading to potential mutation of configuration state, or bypassing the Source field stamping logic.

**Why it happens:** The naming similarity between Options and DTOs, and the temptation to skip the conversion step.

**How to avoid:** The naming convention is intentional: `*Options` classes live in `Simetra.Configuration` namespace and are mutable binding targets. `*Dto` classes live in `Simetra.Models` namespace and are immutable records. The extractor interface should accept ONLY `PollDefinitionDto`, never `MetricPollOptions`.

**Warning signs:** The extractor or pipeline code directly referencing `Simetra.Configuration` namespace types.

### Pitfall 5: Missing varbinds in extraction

**What goes wrong:** A varbind OID is not found in the PollDefinitionDto's Oids list. This can happen with traps (which may include standard trap OIDs like sysUpTime that are not in the definition), or with poll responses that include extra data.

**Why it happens:** SNMP traps always include sysUpTime.0 and snmpTrapOID.0 varbinds. Poll responses may include the requested OIDs plus additional context.

**How to avoid:** The extractor should silently skip varbinds that do not match any OidEntryDto. This is not an error -- it is expected behavior for traps especially. Log at Debug level for visibility but do not fail extraction.

**Warning signs:** Exceptions thrown during extraction for traps, or incomplete ExtractionResults.

### Pitfall 6: EXTR-08 scope confusion

**What goes wrong:** Trying to produce HeartbeatData, PortStatusData, etc. in the generic extractor, violating EXTR-06 (no per-device-type logic).

**Why it happens:** EXTR-08 says "extractor produces strongly typed domain objects per device type" which seems to contradict EXTR-06 "no per-device-type logic."

**How to avoid:** The reconciliation is: the generic extractor produces `ExtractionResult` (generic, role-based). Device modules (Phase 5) provide a mapping function that converts `ExtractionResult` into their specific domain object type (e.g., `HeartbeatData`). Phase 2 should define the base extraction framework and an `ExtractionResult` type. Phase 5 adds the device-specific domain objects. For Phase 2, EXTR-08 is satisfied by designing ExtractionResult to carry all the data needed for downstream strongly-typed conversion -- the "production" happens across Phases 2+5 together.

## Code Examples

Verified patterns from official sources:

### SharpSnmpLib Variable Access
```csharp
// Source: SharpSnmpLib GitHub source (Variable.cs, Integer32.cs, etc.)
// Namespace: Lextm.SharpSnmpLib
using Lextm.SharpSnmpLib;

// A Variable holds an OID and typed data
Variable varbind = /* from trap or poll response */;

// Get the OID as a dotted string
string oidString = varbind.Id.ToString();  // e.g., "1.3.6.1.2.1.2.2.1.7.1"

// Get the typed data
ISnmpData data = varbind.Data;

// Check the type via TypeCode property
SnmpType type = data.TypeCode;  // e.g., SnmpType.Integer32

// Pattern match to extract value
switch (data)
{
    case Integer32 i32:
        int intValue = i32.ToInt32();          // e.g., 1
        break;
    case Counter32 c32:
        uint counter32Value = c32.ToUInt32();  // e.g., 1234567
        break;
    case Counter64 c64:
        ulong counter64Value = c64.ToUInt64(); // e.g., 9876543210
        break;
    case Gauge32 g32:
        uint gaugeValue = g32.ToUInt32();      // e.g., 85
        break;
    case TimeTicks tt:
        uint ticks = tt.ToUInt32();            // e.g., 123456789
        break;
    case OctetString os:
        string strValue = os.ToString();       // e.g., "ge0/1"
        break;
    case IP ip:
        string ipValue = ip.ToString();        // e.g., "10.0.1.1"
        break;
}
```

### Creating Test Variables (for unit tests)
```csharp
// Source: SharpSnmpLib GitHub source (constructors)
using Lextm.SharpSnmpLib;

// Integer32 variable
var intVar = new Variable(
    new ObjectIdentifier("1.3.6.1.2.1.2.2.1.7.1"),
    new Integer32(1));

// OctetString variable
var strVar = new Variable(
    new ObjectIdentifier("1.3.6.1.2.1.31.1.1.1.1.1"),
    new OctetString("ge0/1"));

// Counter32 variable
var counterVar = new Variable(
    new ObjectIdentifier("1.3.6.1.2.1.2.2.1.10.1"),
    new Counter32(1234567));

// Counter64 variable
var counter64Var = new Variable(
    new ObjectIdentifier("1.3.6.1.2.1.31.1.1.1.6.1"),
    new Counter64(9876543210));

// Gauge32 variable
var gaugeVar = new Variable(
    new ObjectIdentifier("1.3.6.1.4.1.9999.1.3.1.0"),
    new Gauge32(85));

// TimeTicks variable
var timeTicksVar = new Variable(
    new ObjectIdentifier("1.3.6.1.2.1.1.3.0"),
    new TimeTicks(123456789));

// IP variable
var ipVar = new Variable(
    new ObjectIdentifier("1.3.6.1.4.1.9999.1.4.1.0"),
    new IP("10.0.1.1"));
```

### Generic Extractor Implementation Pattern
```csharp
// Pattern for the SnmpExtractorService
namespace Simetra.Services;

using Lextm.SharpSnmpLib;
using Simetra.Models;

public class SnmpExtractorService : ISnmpExtractor
{
    private readonly ILogger<SnmpExtractorService> _logger;

    public SnmpExtractorService(ILogger<SnmpExtractorService> logger)
    {
        _logger = logger;
    }

    public ExtractionResult Extract(
        IList<Variable> varbinds,
        PollDefinitionDto definition)
    {
        // Build OID lookup dictionary for O(1) matching
        var oidLookup = definition.Oids
            .ToDictionary(o => o.Oid, o => o);

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

                        // Store EnumMap as metadata if present (NOT as metric value)
                        if (entry.EnumMap is { Count: > 0 })
                        {
                            enumMetadata[entry.PropertyName] = entry.EnumMap;
                        }
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

    private long? ExtractNumericValue(ISnmpData data)
    {
        return data switch
        {
            Integer32 i    => i.ToInt32(),
            Counter32 c32  => (long)c32.ToUInt32(),
            Counter64 c64  => (long)c64.ToUInt64(),
            Gauge32 g      => (long)g.ToUInt32(),
            TimeTicks tt   => (long)tt.ToUInt32(),
            _ => null  // Non-numeric type for a Metric role; log warning
        };
    }

    private string? ExtractLabelValue(ISnmpData data, IReadOnlyDictionary<int, string>? enumMap)
    {
        // For labels with EnumMap: map integer to string
        if (enumMap is not null && data is Integer32 i32)
        {
            return enumMap.TryGetValue(i32.ToInt32(), out var mapped)
                ? mapped
                : i32.ToInt32().ToString();  // Fallback: raw int as string
        }

        // For labels without EnumMap: extract raw string value
        return data switch
        {
            OctetString os => os.ToString(),
            IP ip          => ip.ToString(),
            Integer32 i    => i.ToInt32().ToString(),
            _              => data.ToString()
        };
    }
}
```

### ISnmpExtractor Interface
```csharp
namespace Simetra.Pipeline;

using Lextm.SharpSnmpLib;
using Simetra.Models;

/// <summary>
/// Extracts SNMP varbind data into an ExtractionResult using PollDefinitionDto
/// instructions. Same logic for traps and polls -- no per-device-type logic.
/// </summary>
public interface ISnmpExtractor
{
    /// <summary>
    /// Extracts metric values, label values, and metadata from SNMP varbinds.
    /// </summary>
    /// <param name="varbinds">SNMP varbinds from trap or poll response.</param>
    /// <param name="definition">Poll definition with OID entries and roles.</param>
    /// <returns>Extraction result with metrics, labels, and enum-map metadata.</returns>
    ExtractionResult Extract(IList<Variable> varbinds, PollDefinitionDto definition);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual BER encoding/decoding | SharpSnmpLib handles all encoding | SharpSnmpLib 12.x | No need to understand SNMP wire format; use high-level types |
| Mutable DTOs passed through pipeline | Immutable records (C# 9+) | .NET 5+ (2020) | Value equality, `with` expressions, thread safety by design |
| Switch statements on type enums | C# pattern matching expressions | C# 8-11 (2019-2023) | Cleaner, exhaustive type checking, variable binding in one step |
| Options classes used directly in services | Options -> DTO conversion at boundary | Modern .NET pattern | Clean separation, immutability, testability |

**Deprecated/outdated:**
- SharpSnmpLib versions before 12.x: Used different namespace (`SnmpSharpNet`). Current is `Lextm.SharpSnmpLib`.
- `Counter32` inherits from nothing special; `Gauge32` internally uses `Counter32` for storage but is a distinct type.

## Open Questions

1. **ExtractionResult vs. strongly typed domain objects (EXTR-08 timing)**
   - What we know: The generic extractor produces ExtractionResult (generic). Device modules (Phase 5) convert to typed objects (HeartbeatData, etc.).
   - What's unclear: Should Phase 2 define a base `IDomainObject` interface that Phase 5 domain objects implement? Or should the conversion be entirely Phase 5's responsibility?
   - Recommendation: Phase 2 should NOT define device-specific domain objects. It should produce `ExtractionResult` only. Phase 5 will define `HeartbeatData` etc. and the conversion from `ExtractionResult`. If a base interface is useful, Phase 5 can define it when the concrete types are known. This keeps Phase 2 focused and avoids premature abstraction.

2. **String representation for non-numeric Metric OIDs**
   - What we know: EXTR-04 says "metric value from raw SNMP integer." What if a Role:Metric OID returns a STRING type?
   - What's unclear: Should the extractor reject non-numeric data for Metric role, or attempt conversion?
   - Recommendation: Log a warning and skip the metric. Metric values must be numeric for OTLP. A STRING in a Metric role is a configuration error in the PollDefinitionDto. The extractor should return `null` for the metric value and log the issue.

3. **PollDefinitionDto factory method location**
   - What we know: Config-loaded polls use `PollDefinitionDto.FromOptions(MetricPollOptions)`. Module-defined polls create `PollDefinitionDto` directly.
   - What's unclear: Where does the `FromOptions` conversion happen? In the DI pipeline? In a startup service? In the device registry?
   - Recommendation: Define the `FromOptions` static method on `PollDefinitionDto` itself. The actual invocation happens in Phase 5 (device registry) or Phase 9 (startup sequence) when poll definitions are merged. Phase 2 just provides the conversion method.

## Sources

### Primary (HIGH confidence)
- [SharpSnmpLib GitHub source](https://github.com/lextudio/sharpsnmplib) -- Variable.cs, Integer32.cs, Counter32.cs, Counter64.cs, Gauge32.cs, TimeTicks.cs, IP.cs, OctetString.cs, ObjectIdentifier.cs, ISnmpData.cs, SnmpType.cs all read directly from raw source files
- [Lextm.SharpSnmpLib NuGet 12.5.7](https://www.nuget.org/packages/Lextm.SharpSnmpLib/) -- version, target frameworks (net8.0 + net4.7.1), no dependencies, release date 2025-11-03
- Phase 1 codebase analysis -- MetricPollOptions.cs, OidEntryOptions.cs, MetricType.cs, OidRole.cs, MetricPollSource.cs, ServiceCollectionExtensions.cs all read from local repository
- Project design document (`requirements and basic design.txt`) -- Layer 3 extraction specification, PollDefinitionDto structure, Role-based extraction logic, SNMP type mapping table

### Secondary (MEDIUM confidence)
- [SharpSnmpLib documentation](https://docs.lextudio.com/sharpsnmplib/) -- redirected from docs.sharpsnmp.com, confirms API reference at help.sharpsnmp.com
- .NET 9.0 compatibility with net8.0-targeted packages -- standard forward compatibility guaranteed by .NET versioning policy

### Tertiary (LOW confidence)
- None. All findings verified against primary sources.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- SharpSnmpLib is the only viable .NET SNMP library, version verified on NuGet, source code read directly
- Architecture: HIGH -- patterns derived from existing Phase 1 codebase conventions, design document specifications, and standard .NET practices
- Pitfalls: HIGH -- identified from direct source code analysis of SharpSnmpLib types (uint vs. int overflow, OctetString encoding) and from design document analysis (EXTR-06 vs. EXTR-08 tension)

**Research date:** 2026-02-15
**Valid until:** 2026-03-17 (30 days -- stable domain, no fast-moving dependencies)
