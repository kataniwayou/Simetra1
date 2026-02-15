using Microsoft.Extensions.Options;
using Simetra.Configuration;
using Simetra.Configuration.Validators;

namespace Simetra.Extensions;

/// <summary>
/// Extension methods for registering Simetra configuration options with DI.
/// </summary>
public static class ServiceCollectionExtensions
{
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
}
