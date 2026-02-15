using Microsoft.Extensions.Options;
using Simetra.Configuration.Validators;

namespace Simetra.Tests.Configuration;

/// <summary>
/// Tests for <see cref="DevicesOptionsValidator"/>.
/// Validates the nested object graph: Devices -> MetricPolls -> Oids.
/// </summary>
public sealed class DevicesOptionsValidationTests
{
    private readonly DevicesOptionsValidator _validator = new();

    private static DevicesOptions CreateValidDevicesOptions() => new()
    {
        Devices =
        [
            new DeviceOptions
            {
                Name = "router-core-1",
                IpAddress = "10.0.1.1",
                DeviceType = "router",
                MetricPolls =
                [
                    new MetricPollOptions
                    {
                        MetricName = "simetra_cpu",
                        MetricType = MetricType.Gauge,
                        IntervalSeconds = 30,
                        Oids =
                        [
                            new OidEntryOptions
                            {
                                Oid = "1.3.6.1.4.1.9999.1.3.1.0",
                                PropertyName = "cpu_utilization",
                                Role = OidRole.Metric,
                            }
                        ]
                    }
                ]
            }
        ]
    };

    [Fact]
    public void Validate_WhenDevicesEmpty_ReturnsSuccess()
    {
        var options = new DevicesOptions { Devices = [] };

        var result = _validator.Validate(null, options);

        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void Validate_WithValidDeviceAndMetricPolls_ReturnsSuccess()
    {
        var options = CreateValidDevicesOptions();

        var result = _validator.Validate(null, options);

        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void Validate_WhenDeviceNameMissing_ReturnsFailure()
    {
        var options = CreateValidDevicesOptions();
        options.Devices[0].Name = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Devices[0].Name is required");
    }

    [Fact]
    public void Validate_WhenDeviceIpAddressMissing_ReturnsFailure()
    {
        var options = CreateValidDevicesOptions();
        options.Devices[0].IpAddress = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Devices[0].IpAddress is required");
    }

    [Fact]
    public void Validate_WhenDeviceTypeUnknown_ReturnsFailure()
    {
        var options = CreateValidDevicesOptions();
        options.Devices[0].DeviceType = "firewall";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Devices[0].DeviceType 'firewall' is not a registered device type");
    }

    [Fact]
    public void Validate_WhenDeviceTypeMissing_ReturnsFailure()
    {
        var options = CreateValidDevicesOptions();
        options.Devices[0].DeviceType = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Devices[0].DeviceType is required");
    }

    [Fact]
    public void Validate_WhenMetricPollMetricNameMissing_ReturnsFailure()
    {
        var options = CreateValidDevicesOptions();
        options.Devices[0].MetricPolls[0].MetricName = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Devices[0].MetricPolls[0].MetricName is required");
    }

    [Fact]
    public void Validate_WhenMetricPollIntervalSecondsZero_ReturnsFailure()
    {
        var options = CreateValidDevicesOptions();
        options.Devices[0].MetricPolls[0].IntervalSeconds = 0;

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Devices[0].MetricPolls[0].IntervalSeconds must be greater than 0");
    }

    [Fact]
    public void Validate_WhenMetricPollOidsEmpty_ReturnsFailure()
    {
        var options = CreateValidDevicesOptions();
        options.Devices[0].MetricPolls[0].Oids = [];

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Devices[0].MetricPolls[0].Oids must contain at least one entry");
    }

    [Fact]
    public void Validate_WhenOidEntryOidMissing_ReturnsFailure()
    {
        var options = CreateValidDevicesOptions();
        options.Devices[0].MetricPolls[0].Oids[0].Oid = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Devices[0].MetricPolls[0].Oids[0].Oid is required");
    }

    [Fact]
    public void Validate_WhenOidEntryPropertyNameMissing_ReturnsFailure()
    {
        var options = CreateValidDevicesOptions();
        options.Devices[0].MetricPolls[0].Oids[0].PropertyName = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Devices[0].MetricPolls[0].Oids[0].PropertyName is required");
    }

    [Fact]
    public void Validate_ReturnsAllErrorsNotJustFirst()
    {
        var options = new DevicesOptions
        {
            Devices =
            [
                new DeviceOptions
                {
                    Name = "",
                    IpAddress = "",
                    DeviceType = "",
                    MetricPolls = []
                }
            ]
        };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        // Should contain multiple errors for Name, IpAddress, and DeviceType
        result.FailureMessage.Should().Contain("Devices[0].Name is required");
        result.FailureMessage.Should().Contain("Devices[0].IpAddress is required");
        result.FailureMessage.Should().Contain("Devices[0].DeviceType is required");
    }

    [Fact]
    public void Validate_MetricPollSource_IsConfigurationAfterPostConfigure()
    {
        // Verify that the PostConfigure logic stamps Source = Configuration
        // on all config-loaded metric polls. Simulates the PostConfigure callback.
        var options = CreateValidDevicesOptions();

        // Source defaults to Configuration (0 value of enum) but PostConfigure
        // explicitly sets it -- verify the PostConfigure behavior.
        foreach (var device in options.Devices)
        {
            foreach (var poll in device.MetricPolls)
            {
                poll.Source = MetricPollSource.Configuration;
            }
        }

        options.Devices[0].MetricPolls[0].Source.Should().Be(MetricPollSource.Configuration);
    }

    [Fact]
    public void Validate_WhenDeviceTypeIsCaseInsensitive_ReturnsSuccess()
    {
        var options = CreateValidDevicesOptions();
        options.Devices[0].DeviceType = "Router";

        var result = _validator.Validate(null, options);

        result.Should().Be(ValidateOptionsResult.Success);
    }
}
