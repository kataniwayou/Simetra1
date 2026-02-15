using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Simetra.Extensions;
using Simetra.Pipeline;

var builder = WebApplication.CreateBuilder(args);

builder.AddSimetraTelemetry();
builder.Services.AddSimetraConfiguration(builder.Configuration);
builder.Services.AddDeviceModules();
builder.Services.AddSnmpPipeline();
builder.Services.AddProcessingPipeline();
builder.Services.AddScheduling(builder.Configuration);

builder.Services.AddHealthChecks();

var app = builder.Build();

// LIFE-02: Generate first correlationId directly on startup before any job fires
var correlationService = app.Services.GetRequiredService<ICorrelationService>();
correlationService.SetCorrelationId(Guid.NewGuid().ToString("N"));

// TELEM-05: ForceFlush during shutdown prevents final metric/trace loss.
// Fires on ApplicationStopping (before DI disposal). 5-second timeout budget per provider.
// LoggerProvider flush is handled by the host's logging infrastructure disposal.
app.Lifetime.ApplicationStopping.Register(() =>
{
    // Resolve providers via GetService (null-safe -- providers may not be registered in test scenarios)
    var meterProvider = app.Services.GetService<MeterProvider>();
    var tracerProvider = app.Services.GetService<TracerProvider>();

    meterProvider?.ForceFlush(timeoutMilliseconds: 5000);
    tracerProvider?.ForceFlush(timeoutMilliseconds: 5000);
});

// Health probe endpoints (skeleton -- actual checks added in Phase 9)
app.MapHealthChecks("/healthz/startup");
app.MapHealthChecks("/healthz/ready");
app.MapHealthChecks("/healthz/live");

app.Run();
