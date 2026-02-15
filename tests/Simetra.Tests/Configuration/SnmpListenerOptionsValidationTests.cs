using Microsoft.Extensions.Options;
using Simetra.Configuration.Validators;

namespace Simetra.Tests.Configuration;

/// <summary>
/// Tests for <see cref="SnmpListenerOptionsValidator"/>.
/// </summary>
public sealed class SnmpListenerOptionsValidationTests
{
    private readonly SnmpListenerOptionsValidator _validator = new();

    private static SnmpListenerOptions CreateValid() => new()
    {
        BindAddress = "0.0.0.0",
        Port = 162,
        CommunityString = "public",
        Version = "v2c",
    };

    [Fact]
    public void Validate_WhenAllFieldsValid_ReturnsSuccess()
    {
        var options = CreateValid();

        var result = _validator.Validate(null, options);

        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void Validate_WhenVersionIsNotV2c_ReturnsFailure()
    {
        var options = CreateValid();
        options.Version = "v3";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SnmpListener:Version must be 'v2c'");
    }

    [Fact]
    public void Validate_WhenCommunityStringMissing_ReturnsFailure()
    {
        var options = CreateValid();
        options.CommunityString = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SnmpListener:CommunityString is required");
    }

    [Fact]
    public void Validate_WhenBindAddressMissing_ReturnsFailure()
    {
        var options = CreateValid();
        options.BindAddress = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SnmpListener:BindAddress is required");
    }

    [Fact]
    public void Validate_WhenPortOutOfRange_ReturnsFailure()
    {
        var options = CreateValid();
        options.Port = 0;

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SnmpListener:Port must be between 1 and 65535");
    }
}
