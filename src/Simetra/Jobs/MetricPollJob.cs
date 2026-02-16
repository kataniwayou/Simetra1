using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Simetra.Configuration;
using Simetra.Pipeline;

namespace Simetra.Jobs;

/// <summary>
/// Polls a device for metric data (Source=Configuration) using SNMP GET, extracts results
/// via the generic extractor, and feeds them to the processing pipeline.
/// Bypasses Layer 2 channels and feeds directly to Layer 3/4 (PIPE-06).
/// </summary>
[DisallowConcurrentExecution]
public sealed class MetricPollJob : IJob
{
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly IPollDefinitionRegistry _pollRegistry;
    private readonly ISnmpExtractor _extractor;
    private readonly IProcessingCoordinator _coordinator;
    private readonly ICorrelationService _correlation;
    private readonly ILivenessVectorService _liveness;
    private readonly SnmpListenerOptions _listenerOptions;
    private readonly ILogger<MetricPollJob> _logger;

    public MetricPollJob(
        IDeviceRegistry deviceRegistry,
        IPollDefinitionRegistry pollRegistry,
        ISnmpExtractor extractor,
        IProcessingCoordinator coordinator,
        ICorrelationService correlation,
        ILivenessVectorService liveness,
        IOptions<SnmpListenerOptions> listenerOptions,
        ILogger<MetricPollJob> logger)
    {
        _deviceRegistry = deviceRegistry;
        _pollRegistry = pollRegistry;
        _extractor = extractor;
        _coordinator = coordinator;
        _correlation = correlation;
        _liveness = liveness;
        _listenerOptions = listenerOptions.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        // SCHED-08: Read correlationId BEFORE execution
        var correlationId = _correlation.CurrentCorrelationId;
        var jobKey = context.JobDetail.Key.Name;

        try
        {
            var deviceName = context.MergedJobDataMap.GetString("deviceName")!;
            var metricName = context.MergedJobDataMap.GetString("metricName")!;

            if (!_deviceRegistry.TryGetDeviceByName(deviceName, out var device))
            {
                _logger.LogWarning(
                    "Metric poll job {JobKey}: device {DeviceName} not found in registry",
                    jobKey, deviceName);
                return;
            }

            if (!_pollRegistry.TryGetDefinition(deviceName, metricName, out var definition))
            {
                _logger.LogWarning(
                    "Metric poll job {JobKey}: definition {MetricName} not found for device {DeviceName}",
                    jobKey, metricName, deviceName);
                return;
            }

            var variables = definition.Oids
                .Select(o => new Variable(new ObjectIdentifier(o.Oid)))
                .ToList();

            var endpoint = new IPEndPoint(IPAddress.Parse(device.IpAddress), 161);
            var community = new OctetString(_listenerOptions.CommunityString);

            IList<Variable> response = await Messenger.GetAsync(
                VersionCode.V2,
                endpoint,
                community,
                variables,
                context.CancellationToken);

            // Extract + Process (Layer 3/4 -- bypass Layer 2 channels per PIPE-06)
            var result = _extractor.Extract(response, definition);
            _coordinator.Process(result, device, correlationId);

            _logger.LogDebug(
                "Metric poll completed for {DeviceName}/{MetricName} [CorrelationId: {CorrelationId}]",
                deviceName, metricName, correlationId);
        }
        catch (OperationCanceledException)
        {
            // Shutdown signal -- let it propagate
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Metric poll job {JobKey} failed [CorrelationId: {CorrelationId}]",
                jobKey, correlationId);
        }
        finally
        {
            // SCHED-08: Stamp liveness vector on completion (always, even on failure)
            _liveness.Stamp(jobKey);
        }
    }
}
