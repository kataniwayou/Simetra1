using System.Diagnostics;
using k8s;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Quartz;
using Simetra.Configuration;
using Simetra.Configuration.Validators;
using Simetra.Devices;
using Simetra.Jobs;
using Simetra.Pipeline;
using Simetra.Pipeline.Middleware;
using Simetra.Services;
using Simetra.Telemetry;

namespace Simetra.Extensions;

/// <summary>
/// Extension methods for registering Simetra configuration and pipeline services with DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers OpenTelemetry MeterProvider, TracerProvider, and LoggerProvider with
    /// OTLP exporters, the ILeaderElection abstraction, and the log enrichment processor.
    /// Must be called FIRST in DI registration (registered first = disposed last,
    /// ensuring ForceFlush during shutdown).
    /// <para>
    /// Leader election: auto-detects Kubernetes in-cluster environment via
    /// <see cref="KubernetesClientConfiguration.IsInCluster"/>. In-cluster uses
    /// <see cref="K8sLeaseElection"/> with coordination.k8s.io/v1 Lease API;
    /// local dev uses <see cref="AlwaysLeaderElection"/> (always reports leader).
    /// </para>
    /// <para>
    /// Logging configuration: default providers (Console, Debug, EventSource) are cleared.
    /// Console is re-added only when <see cref="LoggingOptions.EnableConsole"/> is true.
    /// OTLP log exporter is always active (not role-gated -- all pods export logs).
    /// </para>
    /// </summary>
    public static IHostApplicationBuilder AddSimetraTelemetry(
        this IHostApplicationBuilder builder)
    {
        var otlpOptions = new OtlpOptions { Endpoint = "", ServiceName = "" };
        builder.Configuration.GetSection(OtlpOptions.SectionName).Bind(otlpOptions);

        var loggingOptions = new LoggingOptions();
        builder.Configuration.GetSection(LoggingOptions.SectionName).Bind(loggingOptions);

        // --- Leader Election ---
        // Auto-detect Kubernetes in-cluster vs local dev environment.
        // In-cluster: K8sLeaseElection (coordination.k8s.io/v1 Lease API)
        // Local dev: AlwaysLeaderElection (always reports leader)
        if (KubernetesClientConfiguration.IsInCluster())
        {
            // Production: Kubernetes Lease-based leader election
            var kubeConfig = KubernetesClientConfiguration.InClusterConfig();
            builder.Services.AddSingleton<IKubernetes>(new Kubernetes(kubeConfig));

            // Register concrete singleton FIRST, then resolve for both interfaces.
            // This ensures a SINGLE instance serves ILeaderElection and IHostedService
            // (avoids two-instance pitfall where hosted service updates one instance
            // but consumers read from a different one).
            builder.Services.AddSingleton<K8sLeaseElection>();
            builder.Services.AddSingleton<ILeaderElection>(sp =>
                sp.GetRequiredService<K8sLeaseElection>());
            builder.Services.AddHostedService(sp =>
                sp.GetRequiredService<K8sLeaseElection>());
        }
        else
        {
            // Local dev: always leader (single instance, no Kubernetes dependency)
            builder.Services.AddSingleton<ILeaderElection, AlwaysLeaderElection>();
        }

        // --- Metrics + Tracing ---
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: otlpOptions.ServiceName))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(TelemetryConstants.MeterName);
                metrics.AddRuntimeInstrumentation();

                // Manual OTLP metric exporter wrapped in RoleGatedExporter.
                // Cannot use AddOtlpExporter() because it creates and registers the exporter
                // internally, preventing wrapping with RoleGatedExporter. (HA-03, HA-04)
                metrics.AddReader(sp =>
                {
                    var leaderElection = sp.GetRequiredService<ILeaderElection>();
                    var otlpExporter = new OtlpMetricExporter(new OtlpExporterOptions
                    {
                        Endpoint = new Uri(otlpOptions.Endpoint)
                    });
                    var roleGated = new RoleGatedExporter<Metric>(otlpExporter, leaderElection);
                    return new PeriodicExportingMetricReader(roleGated);
                });
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(TelemetryConstants.TracingSourceName);

                // Manual OTLP trace exporter wrapped in RoleGatedExporter.
                // Same rationale as metrics -- AddOtlpExporter() prevents wrapping. (HA-03, HA-04)
                tracing.AddProcessor(sp =>
                {
                    var leaderElection = sp.GetRequiredService<ILeaderElection>();
                    var otlpExporter = new OtlpTraceExporter(new OtlpExporterOptions
                    {
                        Endpoint = new Uri(otlpOptions.Endpoint)
                    });
                    var roleGated = new RoleGatedExporter<Activity>(otlpExporter, leaderElection);
                    return new BatchActivityExportProcessor(roleGated);
                });
            });

        // --- Logging ---
        // Clear default providers (Console, Debug, EventSource) so that
        // EnableConsole=false produces zero stdout output.
        builder.Logging.ClearProviders();

        // Conditionally re-add standard .NET console provider
        if (loggingOptions.EnableConsole)
        {
            builder.Logging.AddConsole();
        }

        // OTLP log exporter: active on ALL pods (not role-gated -- TELEM-04).
        // Enrichment processor adds site/role/correlationId to every log record.
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            logging.SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(serviceName: otlpOptions.ServiceName));
            logging.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpOptions.Endpoint);
            });
            logging.AddProcessor(sp =>
            {
                var siteOptions = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
                var correlationService = sp.GetRequiredService<ICorrelationService>();
                var leaderElection = sp.GetRequiredService<ILeaderElection>();
                return new SimetraLogEnrichmentProcessor(
                    correlationService,
                    siteOptions.Name,
                    () => leaderElection.CurrentRole);
            });
        });

        return builder;
    }

    /// <summary>
    /// Registers all Simetra configuration Options classes, validators, and PostConfigure
    /// callbacks. All options use ValidateOnStart for fail-fast behavior.
    /// </summary>
    public static IServiceCollection AddSimetraConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Flat options with DataAnnotations validation ---

        services.AddOptions<SiteOptions>()
            .Bind(configuration.GetSection(SiteOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LeaseOptions>()
            .Bind(configuration.GetSection(LeaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SnmpListenerOptions>()
            .Bind(configuration.GetSection(SnmpListenerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<HeartbeatJobOptions>()
            .Bind(configuration.GetSection(HeartbeatJobOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CorrelationJobOptions>()
            .Bind(configuration.GetSection(CorrelationJobOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LivenessOptions>()
            .Bind(configuration.GetSection(LivenessOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ChannelsOptions>()
            .Bind(configuration.GetSection(ChannelsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OtlpOptions>()
            .Bind(configuration.GetSection(OtlpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LoggingOptions>()
            .Bind(configuration.GetSection(LoggingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // --- DevicesOptions: custom binding for top-level JSON array ---

        services.AddOptions<DevicesOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                config.GetSection(DevicesOptions.SectionName).Bind(options.Devices);
            })
            .ValidateOnStart();

        // --- IValidateOptions validators (singleton) ---

        services.AddSingleton<IValidateOptions<SiteOptions>, SiteOptionsValidator>();
        services.AddSingleton<IValidateOptions<LeaseOptions>, LeaseOptionsValidator>();
        services.AddSingleton<IValidateOptions<SnmpListenerOptions>, SnmpListenerOptionsValidator>();
        services.AddSingleton<IValidateOptions<DevicesOptions>, DevicesOptionsValidator>();
        services.AddSingleton<IValidateOptions<OtlpOptions>, OtlpOptionsValidator>();

        // --- PostConfigure: SiteOptions PodIdentity default ---

        services.PostConfigure<SiteOptions>(options =>
        {
            options.PodIdentity ??= Environment.GetEnvironmentVariable("HOSTNAME")
                                    ?? Environment.MachineName;
        });

        // --- PostConfigure: Set Source = Configuration on all config-loaded MetricPolls ---

        services.PostConfigure<DevicesOptions>(options =>
        {
            foreach (var device in options.Devices)
            {
                foreach (var poll in device.MetricPolls)
                {
                    poll.Source = MetricPollSource.Configuration;
                }
            }
        });

        return services;
    }

    /// <summary>
    /// Registers all device module implementations as <see cref="IDeviceModule"/> singletons.
    /// Must be called before <see cref="AddSnmpPipeline"/> so that
    /// <c>IEnumerable&lt;IDeviceModule&gt;</c> is available when DeviceRegistry and
    /// DeviceChannelManager resolve.
    /// </summary>
    public static IServiceCollection AddDeviceModules(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceModule, SimetraModule>();

        // Future modules:
        // services.AddSingleton<IDeviceModule, RouterModule>();
        // services.AddSingleton<IDeviceModule, SwitchModule>();

        return services;
    }

    /// <summary>
    /// Registers all Phase 3 SNMP pipeline services: device registry, trap filter,
    /// channel manager, extractor, middleware pipeline, and the listener hosted service.
    /// Must be called after <see cref="AddSimetraConfiguration"/> (services depend on IOptions).
    /// </summary>
    public static IServiceCollection AddSnmpPipeline(this IServiceCollection services)
    {
        // Pipeline infrastructure (singletons -- live for app lifetime)
        services.AddSingleton<ICorrelationService, RotatingCorrelationService>();
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<ITrapFilter, TrapFilter>();
        services.AddSingleton<IDeviceChannelManager, DeviceChannelManager>();
        services.AddSingleton<ISnmpExtractor, SnmpExtractorService>();

        // Middleware (singletons -- stateless)
        services.AddSingleton<ErrorHandlingMiddleware>();
        services.AddSingleton<CorrelationIdMiddleware>();
        services.AddSingleton<LoggingMiddleware>();

        // Build the middleware pipeline as a singleton delegate
        services.AddSingleton<TrapMiddlewareDelegate>(sp =>
        {
            var builder = new TrapPipelineBuilder();
            // Order matters: error handling outermost, then correlationId, then logging
            builder.Use(sp.GetRequiredService<ErrorHandlingMiddleware>());
            builder.Use(sp.GetRequiredService<CorrelationIdMiddleware>());
            builder.Use(sp.GetRequiredService<LoggingMiddleware>());
            return builder.Build();
        });

        // Hosted service (the listener)
        services.AddHostedService<SnmpListenerService>();

        return services;
    }

    /// <summary>
    /// Registers all Phase 4 processing pipeline services: metric factory, state vector,
    /// and processing coordinator. Must be called after <see cref="AddSimetraConfiguration"/>
    /// (MetricFactory depends on <c>IOptions&lt;SiteOptions&gt;</c>).
    /// </summary>
    public static IServiceCollection AddProcessingPipeline(this IServiceCollection services)
    {
        services.AddSingleton<IMetricFactory, MetricFactory>();
        services.AddSingleton<IStateVectorService, StateVectorService>();
        services.AddSingleton<IProcessingCoordinator, ProcessingCoordinator>();

        return services;
    }

    /// <summary>
    /// Registers the Quartz.NET scheduler, all job types, liveness vector service,
    /// and poll definition registry. Configures static jobs (heartbeat, correlation) and
    /// dynamic jobs (state polls from modules, metric polls from configuration) with
    /// appropriate triggers and misfire handling.
    /// Must be called after <see cref="AddSnmpPipeline"/> and <see cref="AddDeviceModules"/>.
    /// </summary>
    public static IServiceCollection AddScheduling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Phase 6 services ---
        services.AddSingleton<ILivenessVectorService, LivenessVectorService>();
        services.AddSingleton<IPollDefinitionRegistry, PollDefinitionRegistry>();

        // --- Bind options for trigger intervals ---
        var heartbeatOptions = new HeartbeatJobOptions();
        configuration.GetSection(HeartbeatJobOptions.SectionName).Bind(heartbeatOptions);

        var correlationOptions = new CorrelationJobOptions();
        configuration.GetSection(CorrelationJobOptions.SectionName).Bind(correlationOptions);

        // --- Read device configuration for dynamic poll job registration ---
        var devicesOptions = new DevicesOptions();
        configuration.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices);

        services.AddQuartz(q =>
        {
            q.UseInMemoryStore();
            q.UseDefaultThreadPool(maxConcurrency: 10);

            // --- Static jobs: Heartbeat ---
            // NOTE on misfire handling (SCHED-10): All triggers use
            // WithMisfireHandlingInstructionNextWithRemainingCount(). This is the
            // correct SimpleTrigger instruction for "skip stale fires, wait for next."
            // The "DoNothing" instruction ONLY exists on CronTrigger and is NOT available
            // on SimpleTrigger. For indefinite RepeatForever triggers, NextWithRemainingCount
            // provides identical semantics. See 06-RESEARCH.md Pitfall 3 for details.
            var heartbeatKey = new JobKey("heartbeat");
            q.AddJob<HeartbeatJob>(j => j.WithIdentity(heartbeatKey));
            q.AddTrigger(t => t
                .ForJob(heartbeatKey)
                .WithIdentity("heartbeat-trigger")
                .StartNow()
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(heartbeatOptions.IntervalSeconds)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionNextWithRemainingCount()));

            // --- Static jobs: Correlation ---
            var correlationKey = new JobKey("correlation");
            q.AddJob<CorrelationJob>(j => j.WithIdentity(correlationKey));
            q.AddTrigger(t => t
                .ForJob(correlationKey)
                .WithIdentity("correlation-trigger")
                .StartNow()
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(correlationOptions.IntervalSeconds)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionNextWithRemainingCount()));

            // --- Dynamic jobs: State polls (Source=Module) ---
            // Device modules are code-defined and known at compile time. Create instances
            // directly for poll definition enumeration at registration time.
            var simetraModule = new SimetraModule();
            var allModules = new IDeviceModule[] { simetraModule };

            foreach (var module in allModules)
            {
                foreach (var poll in module.StatePollDefinitions)
                {
                    var jobKey = new JobKey($"state-poll-{module.DeviceName}-{poll.MetricName}");
                    q.AddJob<StatePollJob>(j => j
                        .WithIdentity(jobKey)
                        .UsingJobData("deviceName", module.DeviceName)
                        .UsingJobData("metricName", poll.MetricName));

                    q.AddTrigger(t => t
                        .ForJob(jobKey)
                        .WithIdentity($"state-poll-{module.DeviceName}-{poll.MetricName}-trigger")
                        .StartNow()
                        .WithSimpleSchedule(s => s
                            .WithIntervalInSeconds(poll.IntervalSeconds)
                            .RepeatForever()
                            .WithMisfireHandlingInstructionNextWithRemainingCount()));
                }
            }

            // --- Dynamic jobs: Metric polls (Source=Configuration) ---
            foreach (var device in devicesOptions.Devices)
            {
                foreach (var poll in device.MetricPolls)
                {
                    var jobKey = new JobKey($"metric-poll-{device.Name}-{poll.MetricName}");
                    q.AddJob<MetricPollJob>(j => j
                        .WithIdentity(jobKey)
                        .UsingJobData("deviceName", device.Name)
                        .UsingJobData("metricName", poll.MetricName));

                    q.AddTrigger(t => t
                        .ForJob(jobKey)
                        .WithIdentity($"metric-poll-{device.Name}-{poll.MetricName}-trigger")
                        .StartNow()
                        .WithSimpleSchedule(s => s
                            .WithIntervalInSeconds(poll.IntervalSeconds)
                            .RepeatForever()
                            .WithMisfireHandlingInstructionNextWithRemainingCount()));
                }
            }
        });

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        return services;
    }
}
