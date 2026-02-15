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

// Health probe endpoints (skeleton -- actual checks added in Phase 9)
app.MapHealthChecks("/healthz/startup");
app.MapHealthChecks("/healthz/ready");
app.MapHealthChecks("/healthz/live");

app.Run();
