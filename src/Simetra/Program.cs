using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
builder.Services.AddSimetraHealthChecks();

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

// Health probe endpoints with tag-filtered checks and explicit status codes.
// Each endpoint runs only the health check(s) matching its tag.
app.MapHealthChecks("/healthz/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.Run();
