var builder = WebApplication.CreateBuilder(args);

// Configuration binding will be added in Plan 02
// builder.Services.AddSimetraConfiguration(builder.Configuration);

builder.Services.AddHealthChecks();

var app = builder.Build();

// Health probe endpoints (skeleton -- actual checks added in Phase 9)
app.MapHealthChecks("/healthz/startup");
app.MapHealthChecks("/healthz/ready");
app.MapHealthChecks("/healthz/live");

app.Run();
