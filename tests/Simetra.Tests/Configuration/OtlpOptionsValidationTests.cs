using Microsoft.Extensions.Options;
using Simetra.Configuration.Validators;

namespace Simetra.Tests.Configuration;

/// <summary>
/// Tests for <see cref="OtlpOptionsValidator"/>.
/// </summary>
public sealed class OtlpOptionsValidationTests
{
    private readonly OtlpOptionsValidator _validator = new();

    private static OtlpOptions CreateValid() => new()
    {
        Endpoint = "http://localhost:4317",
        ServiceName = "simetra-supervisor",
    };

    [Fact]
    public void Validate_WhenAllFieldsValid_ReturnsSuccess()
    {
        var options = CreateValid();

        var result = _validator.Validate(null, options);

        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void Validate_WhenEndpointMissing_ReturnsFailure()
    {
        var options = CreateValid();
        options.Endpoint = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Otlp:Endpoint is required");
    }

    [Fact]
    public void Validate_WhenServiceNameMissing_ReturnsFailure()
    {
        var options = CreateValid();
        options.ServiceName = "";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Otlp:ServiceName is required");
    }
}
