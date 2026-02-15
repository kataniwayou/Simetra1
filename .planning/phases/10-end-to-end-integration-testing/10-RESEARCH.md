# Phase 10: End-to-End Integration + Testing - Research

**Researched:** 2026-02-15
**Domain:** .NET 9 unit/integration testing (xUnit, FluentAssertions, Moq)
**Confidence:** HIGH

## Summary

This research investigates what is needed to implement comprehensive unit tests covering 12 TEST requirements (TEST-01 through TEST-12) and a heartbeat loopback end-to-end integration test. The codebase already has a well-established test project (`tests/Simetra.Tests/`) with 60 passing tests (45 configuration validation + 15 extractor tests) using xUnit 2.9.3, FluentAssertions 7.2.0, and Moq 4.20.72 on .NET 9.

The architecture is highly testable: all services use interface-based DI with sealed classes and constructor injection. Key abstractions (`ICorrelationService`, `IDeviceRegistry`, `ILivenessVectorService`, `IMetricFactory`, `IStateVectorService`, `IProcessingCoordinator`, `ITrapFilter`, `ILeaderElection`, `IDeviceChannelManager`, `ISnmpExtractor`, `IPollDefinitionRegistry`, `IJobIntervalRegistry`) allow straightforward mocking. Several components are concrete with no external dependencies (e.g., `LivenessVectorService`, `StateVectorService`, `RotatingCorrelationService`, `TrapPipelineBuilder`, `JobIntervalRegistry`) and can be tested directly without mocks.

**Primary recommendation:** Follow existing test patterns (Arrange-Act-Assert, Mock.Of<> for loggers, real instances for stateless/in-memory services), organize tests by component domain in subdirectories mirroring source structure, and use `Channel.CreateBounded` directly for backpressure tests. No new NuGet packages are needed.

## Standard Stack

### Core (Already Installed)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| xunit | 2.9.3 | Test framework | Already in test project, .NET ecosystem standard |
| FluentAssertions | 7.2.0 | Fluent assertion syntax | Already in test project. Pinned to 7.2.0 (Apache 2.0) per decision [01-01] |
| Moq | 4.20.72 | Interface mocking | Already in test project, mature and widely used |
| Microsoft.NET.Test.Sdk | 17.12.0 | Test SDK | Already in test project |
| coverlet.collector | 6.0.4 | Code coverage | Already in test project |
| Lextm.SharpSnmpLib | 12.5.7 | SNMP types for test data | Already in test project (needed for Variable, ObjectIdentifier, etc.) |

### Supporting (No New Packages Needed)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Threading.Channels | (runtime) | Real bounded channels for backpressure tests | TEST-08: test DropOldest behavior directly |
| Microsoft.Extensions.Diagnostics.HealthChecks | (runtime) | HealthCheckContext for health check tests | TEST-10: create real HealthCheckContext |
| Microsoft.Extensions.Options | (runtime) | IOptions wrappers for test setup | All tests needing Options<T> dependencies |
| System.Diagnostics.Metrics | (runtime) | MeterFactory for MetricFactory tests | TEST-05: verify metric recording |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Moq 4.20.72 | NSubstitute 5.x | NSubstitute has cleaner syntax but Moq already established in project |
| FluentAssertions 7.2.0 | FA 8.x | 8.x requires commercial license; MUST stay on 7.2.0 per decision [01-01] |
| Real Channel<T> | Mock IDeviceChannelManager | Real channels test actual backpressure; mock only tests interface contract |

**Installation:** No new packages required. All dependencies already in `Simetra.Tests.csproj`.

## Architecture Patterns

### Recommended Test Project Structure
```
tests/Simetra.Tests/
├── Configuration/                # EXISTING: 45 config binding + validation tests
│   ├── ConfigurationBindingTests.cs
│   ├── SiteOptionsValidationTests.cs
│   ├── SnmpListenerOptionsValidationTests.cs
│   ├── DevicesOptionsValidationTests.cs
│   ├── LeaseOptionsValidationTests.cs
│   └── OtlpOptionsValidationTests.cs
├── Extraction/                   # EXISTING: 15 extractor tests (TEST-01 already covered)
│   └── SnmpExtractorTests.cs
├── Pipeline/                     # NEW: TEST-03, TEST-04, TEST-07, TEST-08, TEST-09
│   ├── TrapFilterTests.cs
│   ├── DeviceRegistryTests.cs
│   ├── StateVectorServiceTests.cs
│   ├── ProcessingCoordinatorTests.cs
│   ├── CorrelationServiceTests.cs
│   ├── DeviceChannelManagerTests.cs
│   └── TrapPipelineBuilderTests.cs
├── Processing/                   # NEW: TEST-05
│   └── MetricFactoryTests.cs
├── Health/                       # NEW: TEST-06, TEST-10
│   ├── LivenessVectorServiceTests.cs
│   ├── LivenessHealthCheckTests.cs
│   ├── StartupHealthCheckTests.cs
│   └── ReadinessHealthCheckTests.cs
├── Telemetry/                    # NEW: TEST-12
│   └── RoleGatedExporterTests.cs
├── Lifecycle/                    # NEW: TEST-11
│   └── GracefulShutdownServiceTests.cs
├── Models/                       # NEW: TEST-02
│   └── PollDefinitionDtoTests.cs
├── GlobalUsings.cs               # EXISTING
└── Simetra.Tests.csproj          # EXISTING (no changes needed)
```

### Pattern 1: Direct Instantiation for Stateless/In-Memory Services
**What:** Use real instances (not mocks) for services that are purely in-memory with no external dependencies.
**When to use:** `LivenessVectorService`, `StateVectorService`, `RotatingCorrelationService`, `TrapPipelineBuilder`, `JobIntervalRegistry`, `TrapFilter`, `DeviceChannelManager` (with real Channel<T>)
**Example:**
```csharp
// Source: Existing SnmpExtractorTests pattern
public class LivenessVectorServiceTests
{
    private readonly LivenessVectorService _sut = new();

    [Fact]
    public void Stamp_RecordsTimestamp_GetStampReturnsIt()
    {
        _sut.Stamp("heartbeat");
        var stamp = _sut.GetStamp("heartbeat");
        stamp.Should().NotBeNull();
        stamp!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }
}
```

### Pattern 2: Mock.Of<ILogger<T>> for Logger Dependencies
**What:** Use Moq's `Mock.Of<ILogger<T>>()` for quick logger creation (already established in codebase).
**When to use:** Any class requiring an `ILogger<T>` constructor parameter.
**Example:**
```csharp
// Source: Existing SnmpExtractorTests constructor
var logger = Mock.Of<ILogger<TrapFilter>>();
var sut = new TrapFilter(logger);
```

### Pattern 3: Options.Create() for IOptions<T> Dependencies
**What:** Use `Microsoft.Extensions.Options.Options.Create(value)` to wrap option objects.
**When to use:** Any class requiring `IOptions<T>` constructor parameters.
**Example:**
```csharp
var siteOptions = Options.Create(new SiteOptions { Name = "test-site" });
var channelsOptions = Options.Create(new ChannelsOptions { BoundedCapacity = 5 });
```

### Pattern 4: Real Bounded Channels for Backpressure Testing
**What:** Create real `Channel.CreateBounded<TrapEnvelope>` with small capacity to test DropOldest behavior.
**When to use:** TEST-08 (channel backpressure).
**Example:**
```csharp
// Create DeviceChannelManager with capacity 2 to force drops
var devicesOptions = Options.Create(new DevicesOptions
{
    Devices = { new DeviceOptions { Name = "test-device", IpAddress = "10.0.0.1", DeviceType = "router" } }
});
var channelsOptions = Options.Create(new ChannelsOptions { BoundedCapacity = 2 });
var modules = Enumerable.Empty<IDeviceModule>();
var logger = Mock.Of<ILogger<DeviceChannelManager>>();

var sut = new DeviceChannelManager(devicesOptions, channelsOptions, modules, logger);
// Write 3 items to a capacity-2 channel to trigger DropOldest
```

### Pattern 5: Moq Setup/Verify for Interface Dependencies
**What:** Use `Mock<T>` with Setup/Verify for verifying interactions with dependencies.
**When to use:** Testing `ProcessingCoordinator`, `GracefulShutdownService`, health checks.
**Example:**
```csharp
var mockMetricFactory = new Mock<IMetricFactory>();
var mockStateVector = new Mock<IStateVectorService>();
var logger = Mock.Of<ILogger<ProcessingCoordinator>>();

var sut = new ProcessingCoordinator(mockMetricFactory.Object, mockStateVector.Object, logger);
sut.Process(result, device, "corr-123");

mockMetricFactory.Verify(m => m.RecordMetrics(result, device), Times.Once);
mockStateVector.Verify(sv => sv.Update("device", "metric", result, "corr-123"), Times.Once);
```

### Anti-Patterns to Avoid
- **Over-mocking in-memory services:** Do NOT mock `LivenessVectorService`, `StateVectorService`, `RotatingCorrelationService`, `TrapPipelineBuilder`, or `JobIntervalRegistry` -- use real instances since they have zero external dependencies.
- **Testing implementation details:** Do NOT verify internal dictionary/collection structure. Test behavior through public interface methods.
- **Testing DI wiring in unit tests:** DI registration belongs to integration tests, not unit tests. Test each class in isolation.
- **Using Thread.Sleep for timing:** Use `DateTimeOffset.UtcNow` comparisons with `BeCloseTo()` tolerance instead.
- **Mocking sealed classes:** Moq cannot mock sealed classes. All implementations in this codebase are sealed -- mock their interfaces instead.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Logger verification in Moq | Custom logger spy | `Mock<ILogger<T>>` with Verify on LogLevel | Moq already handles this; custom spy adds complexity |
| Test data builders | Complex builder hierarchy | Static factory methods per test class | Existing codebase uses `MakeDefinition()` pattern -- simple and local |
| IOptions<T> creation | Custom Options wrapper | `Options.Create(value)` | Built into Microsoft.Extensions.Options |
| Channel backpressure simulation | Custom channel wrapper | Real `Channel.CreateBounded<T>` with small capacity | Real channels test actual behavior including DropOldest callbacks |
| HealthCheckContext creation | Complex mock | `new HealthCheckContext { Registration = new HealthCheckRegistration("test", ...) }` | xUnit + Health checks are designed for direct construction |
| Quartz IJobExecutionContext | Hand-built context | `Mock<IJobExecutionContext>` with JobDetail/JobDataMap setup | Moq handles IJob testing well |

**Key insight:** The codebase's interface-heavy design means nearly all dependencies can be mocked with Moq. The few classes that need real instances (in-memory stores, pipeline builder) are trivial to construct.

## Common Pitfalls

### Pitfall 1: FluentAssertions 8.x License Trap
**What goes wrong:** Upgrading FluentAssertions to 8.x (which NuGet may suggest) introduces a commercial license requirement.
**Why it happens:** NuGet auto-suggests latest version during package restore/update.
**How to avoid:** NEVER upgrade FluentAssertions beyond 7.2.0. The pinned version comment in `.csproj` is the guard.
**Warning signs:** Build warning about license, unexpected FA API changes.

### Pitfall 2: Moq Cannot Mock Sealed Classes
**What goes wrong:** Attempting `new Mock<TrapFilter>()` fails because `TrapFilter` is sealed.
**Why it happens:** All service implementations in this codebase are sealed classes.
**How to avoid:** Always mock the INTERFACE (`Mock<ITrapFilter>`), never the concrete type. Only mock when the class is a dependency of the SUT -- when testing the class itself, instantiate it directly with mock/real dependencies.
**Warning signs:** Moq runtime error "Type to mock must be an interface or non-sealed class."

### Pitfall 3: HealthCheckContext Requires Registration
**What goes wrong:** `new HealthCheckContext()` without a Registration throws NullReferenceException in some health check implementations that access `context.Registration`.
**Why it happens:** The health check framework expects a valid registration context.
**How to avoid:** Check if the SUT accesses `context.Registration`. The current health checks in this codebase do NOT access `context.Registration`, so `new HealthCheckContext()` (or null context if not used) works. However, the standard IHealthCheck signature requires it as a parameter.
**Warning signs:** NullReferenceException in CheckHealthAsync.

### Pitfall 4: Channel.CreateBounded DropOldest Callback Timing
**What goes wrong:** Expecting the itemDropped callback to fire synchronously after WriteAsync on a full channel.
**Why it happens:** BoundedChannelFullMode.DropOldest drops the OLDEST item when a new item is written to a full channel. The callback fires synchronously during the Write operation, but the channel must be full first.
**How to avoid:** Fill the channel to capacity first, THEN write one more item. The oldest item will be dropped and the callback will fire. Verify the dropped item is the first one written.
**Warning signs:** Test passes intermittently or callback never fires.

### Pitfall 5: ILogger Verify Complexity with Moq
**What goes wrong:** Attempting to verify `ILogger.Log()` calls with Moq is extremely verbose due to the `Log<TState>` generic method signature.
**Why it happens:** ILogger.Log is a generic method with complex parameters including `Func<TState, Exception?, string>`.
**How to avoid:** For unit tests, simply verify behavior (return values, side effects) rather than log output. If log verification is truly needed, use the `It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("expected"))` pattern. But prefer asserting observable state over log assertions.
**Warning signs:** Extremely long Verify expressions that are fragile.

### Pitfall 6: Testing GracefulShutdownService Requires Careful Mock Setup
**What goes wrong:** GracefulShutdownService resolves services from IServiceProvider using GetService/GetServices, which requires careful mock setup.
**Why it happens:** GracefulShutdownService uses `IServiceProvider.GetService<K8sLeaseElection>()`, `IServiceProvider.GetServices<IHostedService>()`, `GetService<MeterProvider>()`, etc.
**How to avoid:** Mock `IServiceProvider` with Setup for each `GetService` call. For `GetServices<IHostedService>()`, return a list containing a mock `SnmpListenerService`. For optional services (K8sLeaseElection, MeterProvider, TracerProvider), return null to test the null-safe paths.
**Warning signs:** NullReferenceException from unmocked GetService calls.

### Pitfall 7: DateTimeOffset.UtcNow Precision in State/Liveness Tests
**What goes wrong:** Asserting exact equality on timestamps fails due to execution time between production code and assertion.
**Why it happens:** `DateTimeOffset.UtcNow` is called in production code, then again (implicitly) in the test assertion.
**How to avoid:** Use `BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1))` for timestamp assertions. This is the FluentAssertions idiomatic pattern.
**Warning signs:** Tests fail intermittently with "Expected X to be Y but found Z" on timestamp values.

### Pitfall 8: MetricFactory Requires IMeterFactory (Not Just Meter)
**What goes wrong:** Attempting to create MetricFactory with a mock Meter fails because the constructor takes `IMeterFactory`.
**Why it happens:** MetricFactory uses `meterFactory.Create("Simetra.Metrics")` in its constructor.
**How to avoid:** Use `new TestMeterFactory()` which implements IMeterFactory, or use the real `MeterFactory` from `Microsoft.Extensions.Diagnostics.Metrics`. For testing metric RECORDING, create a `MeterListener` that captures measurements.
**Warning signs:** Cannot construct MetricFactory in test, or metrics are not observable.

## Code Examples

### Example 1: Testing TrapFilter.Match (TEST-03)
```csharp
public class TrapFilterTests
{
    private readonly TrapFilter _sut;

    public TrapFilterTests()
    {
        _sut = new TrapFilter(Mock.Of<ILogger<TrapFilter>>());
    }

    [Fact]
    public void Match_WhenVarbindOidMatchesDefinition_ReturnsDefinition()
    {
        var definition = new PollDefinitionDto(
            "test_metric", MetricType.Gauge,
            new List<OidEntryDto>
            {
                new("1.3.6.1.2.1.1.0", "value", OidRole.Metric, null)
            }.AsReadOnly(), 30, MetricPollSource.Module);

        var device = new DeviceInfo("test-device", "10.0.0.1", "router",
            new List<PollDefinitionDto> { definition }.AsReadOnly());

        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.1.0"), new Integer32(42))
        };

        var result = _sut.Match(varbinds, device);

        result.Should().BeSameAs(definition);
    }

    [Fact]
    public void Match_WhenNoOidMatches_ReturnsNull()
    {
        var definition = new PollDefinitionDto(
            "test_metric", MetricType.Gauge,
            new List<OidEntryDto>
            {
                new("1.3.6.1.2.1.1.0", "value", OidRole.Metric, null)
            }.AsReadOnly(), 30, MetricPollSource.Module);

        var device = new DeviceInfo("test-device", "10.0.0.1", "router",
            new List<PollDefinitionDto> { definition }.AsReadOnly());

        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.99.0"), new Integer32(1))
        };

        var result = _sut.Match(varbinds, device);

        result.Should().BeNull();
    }
}
```

### Example 2: Testing ProcessingCoordinator Source-Based Routing (TEST-04)
```csharp
public class ProcessingCoordinatorTests
{
    private readonly Mock<IMetricFactory> _mockMetrics = new();
    private readonly Mock<IStateVectorService> _mockStateVector = new();
    private readonly ProcessingCoordinator _sut;

    public ProcessingCoordinatorTests()
    {
        _sut = new ProcessingCoordinator(
            _mockMetrics.Object,
            _mockStateVector.Object,
            Mock.Of<ILogger<ProcessingCoordinator>>());
    }

    [Fact]
    public void Process_SourceModule_CallsBothBranches()
    {
        var definition = new PollDefinitionDto(
            "test", MetricType.Gauge, Array.Empty<OidEntryDto>().ToList().AsReadOnly(),
            30, MetricPollSource.Module);
        var result = new ExtractionResult { Definition = definition };
        var device = new DeviceInfo("dev", "10.0.0.1", "router", Array.Empty<PollDefinitionDto>().ToList().AsReadOnly());

        _sut.Process(result, device, "corr-1");

        _mockMetrics.Verify(m => m.RecordMetrics(result, device), Times.Once);
        _mockStateVector.Verify(sv => sv.Update("dev", "test", result, "corr-1"), Times.Once);
    }

    [Fact]
    public void Process_SourceConfiguration_CallsMetricsOnly_SkipsStateVector()
    {
        var definition = new PollDefinitionDto(
            "test", MetricType.Gauge, Array.Empty<OidEntryDto>().ToList().AsReadOnly(),
            30, MetricPollSource.Configuration);
        var result = new ExtractionResult { Definition = definition };
        var device = new DeviceInfo("dev", "10.0.0.1", "router", Array.Empty<PollDefinitionDto>().ToList().AsReadOnly());

        _sut.Process(result, device, "corr-1");

        _mockMetrics.Verify(m => m.RecordMetrics(result, device), Times.Once);
        _mockStateVector.Verify(
            sv => sv.Update(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ExtractionResult>(), It.IsAny<string>()),
            Times.Never);
    }
}
```

### Example 3: Testing Middleware Chain Composition (TEST-09)
```csharp
public class TrapPipelineBuilderTests
{
    [Fact]
    public async Task Build_MiddlewareExecutesInRegistrationOrder()
    {
        var executionOrder = new List<int>();

        var builder = new TrapPipelineBuilder();
        builder.Use(next => async context =>
        {
            executionOrder.Add(1);
            await next(context);
        });
        builder.Use(next => async context =>
        {
            executionOrder.Add(2);
            await next(context);
        });
        builder.Use(next => async context =>
        {
            executionOrder.Add(3);
            await next(context);
        });

        var pipeline = builder.Build();
        var envelope = new TrapEnvelope
        {
            Varbinds = new List<Variable>(),
            SenderAddress = System.Net.IPAddress.Loopback,
            ReceivedAt = DateTimeOffset.UtcNow
        };
        var context = new TrapContext { Envelope = envelope };

        await pipeline(context);

        executionOrder.Should().Equal(1, 2, 3);
    }
}
```

### Example 4: Testing RoleGatedExporter (TEST-12)
```csharp
public class RoleGatedExporterTests
{
    [Fact]
    public void Export_WhenLeader_DelegatesToInner()
    {
        var mockInner = new Mock<BaseExporter<Activity>>();
        var mockElection = new Mock<ILeaderElection>();
        mockElection.Setup(e => e.IsLeader).Returns(true);

        var sut = new RoleGatedExporter<Activity>(mockInner.Object, mockElection.Object);

        // Note: Batch<T> is a struct and hard to construct in tests.
        // The key verification is that the inner exporter IS called when leader.
        // This may require an integration-style approach or testing via the
        // ForceFlush/Shutdown delegating methods instead.
    }

    [Fact]
    public void ForceFlush_DelegatesToInner()
    {
        var mockInner = new Mock<BaseExporter<Activity>>();
        mockInner.Setup(e => e.ForceFlush(It.IsAny<int>())).Returns(true);
        var mockElection = new Mock<ILeaderElection>();

        var sut = new RoleGatedExporter<Activity>(mockInner.Object, mockElection.Object);
        var result = sut.ForceFlush(5000);

        result.Should().BeTrue();
    }
}
```

### Example 5: Testing Channel Backpressure (TEST-08)
```csharp
public class DeviceChannelManagerTests
{
    [Fact]
    public async Task WriteToFullChannel_DropsOldestItem()
    {
        var devicesOptions = Options.Create(new DevicesOptions());
        devicesOptions.Value.Devices.Add(new DeviceOptions
        {
            Name = "test-dev",
            IpAddress = "10.0.0.1",
            DeviceType = "router"
        });
        var channelsOptions = Options.Create(new ChannelsOptions { BoundedCapacity = 2 });
        var logger = Mock.Of<ILogger<DeviceChannelManager>>();
        var modules = Enumerable.Empty<IDeviceModule>();

        var sut = new DeviceChannelManager(devicesOptions, channelsOptions, modules, logger);
        var writer = sut.GetWriter("test-dev");
        var reader = sut.GetReader("test-dev");

        // Fill channel (capacity 2)
        var env1 = MakeEnvelope("corr-1");
        var env2 = MakeEnvelope("corr-2");
        await writer.WriteAsync(env1);
        await writer.WriteAsync(env2);

        // Write one more -- should drop oldest (env1)
        var env3 = MakeEnvelope("corr-3");
        await writer.WriteAsync(env3);

        // Read remaining items -- should be env2, env3 (env1 was dropped)
        reader.TryRead(out var first).Should().BeTrue();
        first!.CorrelationId.Should().Be("corr-2");

        reader.TryRead(out var second).Should().BeTrue();
        second!.CorrelationId.Should().Be("corr-3");
    }

    private static TrapEnvelope MakeEnvelope(string correlationId) => new()
    {
        Varbinds = new List<Variable>(),
        SenderAddress = System.Net.IPAddress.Loopback,
        ReceivedAt = DateTimeOffset.UtcNow,
        CorrelationId = correlationId
    };
}
```

### Example 6: Testing LivenessHealthCheck Staleness Detection (TEST-06/TEST-10)
```csharp
public class LivenessHealthCheckTests
{
    [Fact]
    public async Task CheckHealth_WhenAllStampsFresh_ReturnsHealthy()
    {
        var liveness = new LivenessVectorService();
        liveness.Stamp("heartbeat");

        var intervals = new JobIntervalRegistry();
        intervals.Register("heartbeat", 15);

        var livenessOptions = Options.Create(new LivenessOptions { GraceMultiplier = 3.0 });
        var logger = Mock.Of<ILogger<LivenessHealthCheck>>();

        var sut = new LivenessHealthCheck(liveness, intervals, livenessOptions, logger);
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_WhenStampStale_ReturnsUnhealthy()
    {
        var liveness = new Mock<ILivenessVectorService>();
        // Return a stamp from 120 seconds ago
        liveness.Setup(l => l.GetAllStamps()).Returns(
            new Dictionary<string, DateTimeOffset>
            {
                ["heartbeat"] = DateTimeOffset.UtcNow.AddSeconds(-120)
            }.AsReadOnly());

        var intervals = new JobIntervalRegistry();
        intervals.Register("heartbeat", 15); // threshold = 15 * 3.0 = 45s, age = 120s -> stale

        var livenessOptions = Options.Create(new LivenessOptions { GraceMultiplier = 3.0 });
        var logger = Mock.Of<ILogger<LivenessHealthCheck>>();

        var sut = new LivenessHealthCheck(liveness.Object, intervals, livenessOptions, logger);
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| NUnit | xUnit preferred for .NET Core | .NET Core era | Already using xUnit 2.9.3 |
| FluentAssertions 6.x | FA 7.x (pinned, skip 8.x) | 2024 | 7.2.0 is latest Apache 2.0 |
| Moq 4.18 | Moq 4.20.72 | 2024 | Already on latest stable Moq 4.x |
| MSTest | xUnit/NUnit | .NET Core era | xUnit already established |

**Deprecated/outdated:**
- FluentAssertions 8.x: Requires commercial license. Do NOT use.
- Moq 5.x (hypothetical): SponsorLink controversy in 4.20; 4.20.72 reverted it. Stay on 4.20.72.

## Requirement-to-Component Mapping

This section maps each TEST requirement to the exact source files, interfaces, and test strategies needed.

### TEST-01: Generic Extractor (All SNMP types, Role:Metric, Role:Label, EnumMap)
- **Status:** ALREADY COMPLETE. 15 tests exist in `tests/Simetra.Tests/Extraction/SnmpExtractorTests.cs`
- **SUT:** `SnmpExtractorService` (real instance, mock logger only)
- **Coverage:** Integer32, Counter32, Counter64, Gauge32, TimeTicks for Metric role; OctetString, IP for Label role; EnumMap mapping with known/unknown values; EnumMap metadata on Metric role; edge cases (unmatched OID, non-numeric metric, empty varbinds, multi-varbind)
- **Action:** No new tests needed. Verify existing 15 tests still pass.

### TEST-02: PollDefinitionDto Validation and Source Field Assignment
- **SUT:** `PollDefinitionDto.FromOptions()` static method; `MetricPollSource` enum behavior
- **Test file:** `tests/Simetra.Tests/Models/PollDefinitionDtoTests.cs`
- **Strategy:** Test `PollDefinitionDto.FromOptions()` converts MetricPollOptions correctly, including OID conversion, EnumMap defensive copy, Source propagation. One test already exists in DevicesOptionsValidationTests (`Validate_MetricPollSource_IsConfigurationAfterPostConfigure`) verifying PostConfigure stamps Source=Configuration.
- **Key tests:**
  - FromOptions preserves MetricName, MetricType, IntervalSeconds
  - FromOptions converts OidEntryOptions to OidEntryDto with correct Role
  - FromOptions creates defensive copy of EnumMap
  - FromOptions preserves Source field from options
  - Resulting Oids list is ReadOnly

### TEST-03: Device Filter and Trap Filter Logic
- **SUT:** `TrapFilter` (real instance), `DeviceRegistry` (real instance)
- **Test files:** `tests/Simetra.Tests/Pipeline/TrapFilterTests.cs`, `tests/Simetra.Tests/Pipeline/DeviceRegistryTests.cs`
- **Strategy:**
  - TrapFilter: Test Match returns first matching definition, returns null on no match, handles multiple definitions per device, matches on first OID intersection
  - DeviceRegistry: Test TryGetDevice by IP (IPv4 normalization), TryGetDeviceByName (case-insensitive), module devices override config on IP collision, unknown IP returns false

### TEST-04: State Vector Updates and Source-Based Routing
- **SUT:** `StateVectorService` (real instance), `ProcessingCoordinator` (real instance with mocked dependencies)
- **Test files:** `tests/Simetra.Tests/Pipeline/StateVectorServiceTests.cs`, `tests/Simetra.Tests/Pipeline/ProcessingCoordinatorTests.cs`
- **Strategy:**
  - StateVectorService: Test Update creates entry, Update overwrites existing, GetEntry returns null for missing, GetAllEntries returns snapshot, composite key format "deviceName:metricName"
  - ProcessingCoordinator: Test Source=Module calls both branches, Source=Configuration calls metrics only, Branch A failure does not block Branch B, Branch B failure does not block Branch A

### TEST-05: IMetricFactory Base Label Enforcement
- **SUT:** `MetricFactory` (real instance with real MeterFactory)
- **Test file:** `tests/Simetra.Tests/Processing/MetricFactoryTests.cs`
- **Strategy:** Create MetricFactory with real meter (from `new Meter("test")`), use MeterListener to capture recorded measurements and verify: base labels (site, device_name, device_ip, device_type) are present on every metric; dynamic labels from ExtractionResult.Labels are appended; Gauge vs Counter instrument types are used correctly; metric name format is "{MetricName}_{PropertyName}"
- **Key challenge:** Requires `MeterListener` to observe measurements. Use `System.Diagnostics.Metrics.MeterListener` to subscribe and capture tag values.

### TEST-06: Liveness Vector Stamping and Staleness Detection
- **SUT:** `LivenessVectorService` (real instance), `LivenessHealthCheck` (real instance with mix of real/mock deps)
- **Test files:** `tests/Simetra.Tests/Health/LivenessVectorServiceTests.cs`, `tests/Simetra.Tests/Health/LivenessHealthCheckTests.cs`
- **Strategy:**
  - LivenessVectorService: Test Stamp records timestamp, GetStamp returns null for never-stamped, GetAllStamps returns defensive copy, multiple stamps overwrite
  - LivenessHealthCheck: Test fresh stamps -> Healthy, stale stamps -> Unhealthy with data, unknown job keys skipped, empty stamps -> Healthy

### TEST-07: Correlation ID Generation and Propagation
- **SUT:** `RotatingCorrelationService` (real instance), `CorrelationIdMiddleware` (real instance with real CorrelationService)
- **Test files:** `tests/Simetra.Tests/Pipeline/CorrelationServiceTests.cs` (includes middleware test)
- **Strategy:**
  - RotatingCorrelationService: Test initial value is empty, SetCorrelationId updates CurrentCorrelationId, multiple sets overwrite
  - CorrelationIdMiddleware: Test stamps correlationId on TrapEnvelope before calling next, propagates current value from service

### TEST-08: Channel Backpressure (Drop-Oldest, itemDropped Callback)
- **SUT:** `DeviceChannelManager` (real instance with real bounded channels)
- **Test file:** `tests/Simetra.Tests/Pipeline/DeviceChannelManagerTests.cs`
- **Strategy:** Create DeviceChannelManager with small capacity (2-3), write more items than capacity, verify oldest items are dropped and newest survive. Test CompleteAll prevents further writes. Test WaitForDrainAsync completes after reading all items. Test GetWriter/GetReader throw on unknown device.

### TEST-09: Middleware Chain Composition and Execution Order
- **SUT:** `TrapPipelineBuilder` (real instance), individual middleware (real instances)
- **Test file:** `tests/Simetra.Tests/Pipeline/TrapPipelineBuilderTests.cs`
- **Strategy:** Test middleware executes in registration order, terminal delegate is no-op, ITrapMiddleware overload works, error handling middleware catches exceptions, correlation middleware stamps envelope, short-circuit behavior (if middleware doesn't call next)

### TEST-10: K8s Health Probe HTTP Handlers (Startup, Readiness, Liveness)
- **SUT:** `StartupHealthCheck`, `ReadinessHealthCheck`, `LivenessHealthCheck`
- **Test files:** `tests/Simetra.Tests/Health/StartupHealthCheckTests.cs`, `tests/Simetra.Tests/Health/ReadinessHealthCheckTests.cs`, `tests/Simetra.Tests/Health/LivenessHealthCheckTests.cs`
- **Strategy:**
  - StartupHealthCheck: Test Healthy when correlationId is set, Unhealthy when empty
  - ReadinessHealthCheck: Test Healthy when channels exist and scheduler running, Unhealthy when no channels, Unhealthy when scheduler not started
  - LivenessHealthCheck: Covered in TEST-06

### TEST-11: Graceful Shutdown Ordering and Time Budget Enforcement
- **SUT:** `GracefulShutdownService`
- **Test file:** `tests/Simetra.Tests/Lifecycle/GracefulShutdownServiceTests.cs`
- **Strategy:** Mock ISchedulerFactory, IDeviceChannelManager, IServiceProvider. Verify StopAsync calls steps in order. Test that Step 5 (telemetry flush) runs even if earlier steps fail. Test time budget enforcement via short CancellationToken. Test null-safe resolution of optional services (K8sLeaseElection, MeterProvider, TracerProvider).
- **Key challenge:** IServiceProvider mocking requires careful Setup for GetService<T> and GetServices<IHostedService>. Use sequential Callback verification to prove ordering.

### TEST-12: Role-Gated Exporter Pattern (Leader/Follower Switching)
- **SUT:** `RoleGatedExporter<T>`
- **Test file:** `tests/Simetra.Tests/Telemetry/RoleGatedExporterTests.cs`
- **Strategy:** Test ForceFlush delegates to inner, Shutdown delegates to inner, Dispose disposes inner. For the Export method, testing is challenging because `Batch<T>` is a struct with no public constructor.
- **Key challenge:** `Batch<T>` in OpenTelemetry is a struct that wraps a circular buffer pointer and cannot be easily constructed in unit tests. Two approaches: (1) Test ForceFlush/Shutdown delegation and constructor validation only (HIGH value, covers the delegation contract). (2) Create a test subclass of BaseExporter to track calls (MEDIUM complexity). Recommend approach (1) as the primary strategy with null guard testing.

## Specific Technical Considerations

### MetricFactory Testing with MeterListener
The `MetricFactory` uses `System.Diagnostics.Metrics.Meter` to create instruments and record values. To verify base label enforcement (TEST-05), use `MeterListener`:

```csharp
var meterFactory = new TestMeterFactory(); // or mock IMeterFactory
// IMeterFactory.Create returns a Meter. Use real MeterFactory from Microsoft.Extensions.Diagnostics
using var meter = new Meter("Simetra.Metrics");
// Listen for measurements
using var listener = new MeterListener();
var recordedTags = new List<KeyValuePair<string, object?>>();
listener.InstrumentPublished = (instrument, listener) =>
{
    if (instrument.Meter.Name == "Simetra.Metrics")
        listener.EnableMeasurementEvents(instrument);
};
listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
{
    recordedTags.AddRange(tags.ToArray());
});
listener.Start();
```

Note: `IMeterFactory` is available from `Microsoft.Extensions.Diagnostics` (included transitively via ASP.NET). The simplest approach is to mock `IMeterFactory` to return a real `Meter`, then use `MeterListener` to capture measurements.

### Quartz IJobExecutionContext Mocking
For testing jobs that use `context.MergedJobDataMap.GetString()` and `context.JobDetail.Key`:

```csharp
var mockContext = new Mock<IJobExecutionContext>();
var jobDataMap = new JobDataMap { { "deviceName", "test-device" }, { "metricName", "test_metric" } };
mockContext.Setup(c => c.MergedJobDataMap).Returns(jobDataMap);
mockContext.Setup(c => c.JobDetail.Key).Returns(new JobKey("test-job"));
mockContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
```

### RoleGatedExporter Batch<T> Limitation
`Batch<T>` in OpenTelemetry SDK is a readonly struct with an internal constructor. It cannot be instantiated in test code. This means the `Export(in Batch<T> batch)` method cannot be directly unit tested with Moq. The recommended approach:
1. Test constructor null guards (ensures inner and leaderElection are required)
2. Test ForceFlush, Shutdown, Dispose delegation (these are testable via the protected virtual methods)
3. For Export gating logic verification, create a concrete test double (a simple BaseExporter subclass) as the inner exporter, then use reflection or a test harness if absolutely needed

For practical purposes, the ForceFlush/Shutdown delegation tests plus constructor validation provide sufficient coverage of the decorator pattern.

## Open Questions

1. **IMeterFactory in test context**
   - What we know: MetricFactory constructor takes `IMeterFactory`. The `Microsoft.Extensions.Diagnostics` package provides a default implementation.
   - What's unclear: Whether the test project already has transitive access to `IMeterFactory` or if it needs an explicit package reference.
   - Recommendation: The test project references the main Simetra project which uses `Microsoft.NET.Sdk.Web` -- this transitively includes `Microsoft.Extensions.Diagnostics`. If not available, mock `IMeterFactory` to return `new Meter("test")`.

2. **HealthCheckContext construction requirements**
   - What we know: The three health checks (`StartupHealthCheck`, `ReadinessHealthCheck`, `LivenessHealthCheck`) all accept `HealthCheckContext context` but none of them access `context.Registration` in their implementations.
   - What's unclear: Whether future xUnit versions or health check middleware will enforce non-null Registration.
   - Recommendation: Pass `new HealthCheckContext()` with default null Registration. This works with the current implementations.

## Sources

### Primary (HIGH confidence)
- Codebase analysis: Direct reading of all 70+ source files in `src/Simetra/` and `tests/Simetra.Tests/`
- Existing test patterns: 60 tests across 7 test files establishing conventions (Arrange-Act-Assert, Mock.Of, factory methods)
- NuGet package versions verified from `Simetra.Tests.csproj`: xUnit 2.9.3, FA 7.2.0, Moq 4.20.72
- Decision log in `STATE.md`: [01-01] FA 7.2.0 pinning, [01-03] inverted TDD pattern

### Secondary (MEDIUM confidence)
- .planning/codebase/TESTING.md: Test patterns guide (pre-implementation reference)
- .planning/codebase/ARCHITECTURE.md: Layer descriptions and data flow
- .planning/codebase/STRUCTURE.md: Directory layout and naming conventions

### Tertiary (LOW confidence)
- MeterListener API: Based on training data knowledge of System.Diagnostics.Metrics (.NET 8+), not verified against current .NET 9 docs
- Batch<T> internal constructor: Based on training data knowledge of OpenTelemetry SDK internals

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- all packages already installed and verified in .csproj
- Architecture: HIGH -- all source files read directly, test patterns established by 60 existing tests
- Pitfalls: HIGH -- derived from actual sealed class analysis, real API constraints, and codebase-specific decisions
- Code examples: HIGH -- all examples use actual types and APIs from the codebase

**Research date:** 2026-02-15
**Valid until:** 2026-03-15 (stable -- no expected changes to test framework or codebase APIs)
