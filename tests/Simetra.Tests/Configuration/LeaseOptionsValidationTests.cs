using Microsoft.Extensions.Options;
using Simetra.Configuration.Validators;

namespace Simetra.Tests.Configuration;

/// <summary>
/// Tests for <see cref="LeaseOptionsValidator"/>.
/// </summary>
public sealed class LeaseOptionsValidationTests
{
    private readonly LeaseOptionsValidator _validator = new();

    private static LeaseOptions CreateValid() => new()
    {
        Name = "simetra-leader",
        Namespace = "simetra",
        RenewIntervalSeconds = 10,
        DurationSeconds = 15,
    };

    [Fact]
    public void Validate_WhenAllFieldsValid_ReturnsSuccess()
    {
        var options = CreateValid();

        var result = _validator.Validate(null, options);

        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void Validate_WhenDurationLessThanRenewInterval_ReturnsFailure()
    {
        var options = CreateValid();
        options.DurationSeconds = 5;
        options.RenewIntervalSeconds = 10;

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Lease:DurationSeconds must be greater than Lease:RenewIntervalSeconds");
    }

    [Fact]
    public void Validate_WhenDurationEqualToRenewInterval_ReturnsFailure()
    {
        var options = CreateValid();
        options.DurationSeconds = 10;
        options.RenewIntervalSeconds = 10;

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Lease:DurationSeconds must be greater than Lease:RenewIntervalSeconds");
    }

    [Fact]
    public void Validate_WhenNameMissing_ReturnsFailure()
    {
        var options = CreateValid();
        options.Name = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Lease:Name is required");
    }

    [Fact]
    public void Validate_WhenNamespaceMissing_ReturnsFailure()
    {
        var options = CreateValid();
        options.Namespace = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Lease:Namespace is required");
    }
}
