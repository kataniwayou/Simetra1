using Simetra.Models;

namespace Simetra.Pipeline;

/// <summary>
/// Creates and records OTLP-ready metrics from <see cref="ExtractionResult"/> data.
/// Every metric is emitted with enforced base labels (site, device_name, device_ip, device_type)
/// plus dynamic labels derived from Role:Label OID values in the extraction result.
/// Metric names follow the pattern <c>{MetricName}_{PropertyName}</c>.
/// </summary>
public interface IMetricFactory
{
    /// <summary>
    /// Records all numeric metrics from an extraction result as System.Diagnostics.Metrics
    /// measurements, attaching base labels from site configuration and device identity,
    /// plus any dynamic labels from the extraction result.
    /// </summary>
    /// <param name="result">Extraction result containing metrics and labels.</param>
    /// <param name="device">Device identity providing base label values.</param>
    void RecordMetrics(ExtractionResult result, DeviceInfo device);
}
