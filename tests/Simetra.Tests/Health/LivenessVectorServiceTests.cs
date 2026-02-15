using FluentAssertions;
using Simetra.Pipeline;

namespace Simetra.Tests.Health;

public class LivenessVectorServiceTests
{
    private readonly LivenessVectorService _sut = new();

    [Fact]
    public void Stamp_RecordsTimestamp()
    {
        _sut.Stamp("heartbeat");

        var stamp = _sut.GetStamp("heartbeat");

        stamp.Should().NotBeNull();
    }

    [Fact]
    public void Stamp_TimestampIsRecent()
    {
        _sut.Stamp("heartbeat");

        var stamp = _sut.GetStamp("heartbeat");

        stamp.Should().NotBeNull();
        stamp!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetStamp_NeverStamped_ReturnsNull()
    {
        var stamp = _sut.GetStamp("unknown-job");

        stamp.Should().BeNull();
    }

    [Fact]
    public void Stamp_OverwritesPreviousValue()
    {
        _sut.Stamp("heartbeat");
        var first = _sut.GetStamp("heartbeat");

        // Small delay to ensure timestamps differ
        _sut.Stamp("heartbeat");
        var second = _sut.GetStamp("heartbeat");

        second.Should().NotBeNull();
        second!.Value.Should().BeOnOrAfter(first!.Value);
    }

    [Fact]
    public void GetAllStamps_ReturnsAllEntries()
    {
        _sut.Stamp("heartbeat");
        _sut.Stamp("correlation");

        var all = _sut.GetAllStamps();

        all.Should().HaveCount(2);
        all.Should().ContainKey("heartbeat");
        all.Should().ContainKey("correlation");
    }

    [Fact]
    public void GetAllStamps_ReturnsDefensiveCopy()
    {
        _sut.Stamp("heartbeat");
        var snapshot1 = _sut.GetAllStamps();

        _sut.Stamp("new-job");
        var snapshot2 = _sut.GetAllStamps();

        snapshot1.Should().HaveCount(1);
        snapshot1.Should().NotContainKey("new-job");
        snapshot2.Should().HaveCount(2);
        snapshot2.Should().ContainKey("new-job");
    }
}
