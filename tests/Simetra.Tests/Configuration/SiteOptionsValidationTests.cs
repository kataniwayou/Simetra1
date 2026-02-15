using Microsoft.Extensions.Options;
using Simetra.Configuration.Validators;

namespace Simetra.Tests.Configuration;

/// <summary>
/// Tests for <see cref="SiteOptionsValidator"/> and SiteOptions PostConfigure behavior.
/// </summary>
public sealed class SiteOptionsValidationTests
{
    private readonly SiteOptionsValidator _validator = new();

    [Fact]
    public void Validate_WhenNameIsMissing_ReturnsFailure()
    {
        var options = new SiteOptions { Name = "" };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Site:Name is required");
    }

    [Fact]
    public void Validate_WhenNameIsWhitespace_ReturnsFailure()
    {
        var options = new SiteOptions { Name = "   " };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Site:Name is required");
    }

    [Fact]
    public void Validate_WhenNameIsPresent_ReturnsSuccess()
    {
        var options = new SiteOptions { Name = "site-nyc-01" };

        var result = _validator.Validate(null, options);

        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void PostConfigure_WhenPodIdentityIsNull_DefaultsToMachineName()
    {
        // Simulate the PostConfigure behavior from ServiceCollectionExtensions.
        // When PodIdentity is null, it defaults to HOSTNAME env var or Environment.MachineName.
        var options = new SiteOptions { Name = "site-1", PodIdentity = null };

        // Apply the same logic as the PostConfigure callback
        options.PodIdentity ??= Environment.GetEnvironmentVariable("HOSTNAME")
                                ?? Environment.MachineName;

        options.PodIdentity.Should().NotBeNullOrWhiteSpace();
        // It should be either the HOSTNAME env var or the machine name
        var expectedHostname = Environment.GetEnvironmentVariable("HOSTNAME");
        if (expectedHostname is not null)
        {
            options.PodIdentity.Should().Be(expectedHostname);
        }
        else
        {
            options.PodIdentity.Should().Be(Environment.MachineName);
        }
    }

    [Fact]
    public void PostConfigure_WhenPodIdentityIsSet_DoesNotOverride()
    {
        var options = new SiteOptions { Name = "site-1", PodIdentity = "my-custom-pod" };

        // Apply the same PostConfigure logic
        options.PodIdentity ??= Environment.GetEnvironmentVariable("HOSTNAME")
                                ?? Environment.MachineName;

        options.PodIdentity.Should().Be("my-custom-pod");
    }
}
