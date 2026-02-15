namespace Simetra.Telemetry;

/// <summary>
/// Shared constants for OpenTelemetry meter and tracing source names.
/// MeterName MUST match the string used in MetricFactory: meterFactory.Create("Simetra.Metrics").
/// </summary>
public static class TelemetryConstants
{
    /// <summary>
    /// Meter name subscribed to by MeterProvider. Matches MetricFactory's meter creation.
    /// </summary>
    public const string MeterName = "Simetra.Metrics";

    /// <summary>
    /// ActivitySource name subscribed to by TracerProvider.
    /// </summary>
    public const string TracingSourceName = "Simetra.Tracing";
}
