using System.Diagnostics;
using OpenTelemetry;
using Simetra.Telemetry;

namespace Simetra.Tests.Telemetry;

public class RoleGatedExporterTests
{
    private readonly Mock<ILeaderElection> _mockLeaderElection = new();

    /// <summary>
    /// Concrete test double for BaseExporter -- Moq cannot easily override protected methods
    /// on abstract classes that the SUT calls through public API (ForceFlush -> OnForceFlush).
    /// </summary>
    private sealed class TestExporter : BaseExporter<Activity>
    {
        public bool ForceFlushCalled { get; private set; }
        public bool ShutdownCalled { get; private set; }
        public bool DisposeCalled { get; private set; }

        public override ExportResult Export(in Batch<Activity> batch) => ExportResult.Success;

        protected override bool OnForceFlush(int timeoutMilliseconds)
        {
            ForceFlushCalled = true;
            return true;
        }

        protected override bool OnShutdown(int timeoutMilliseconds)
        {
            ShutdownCalled = true;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCalled = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void Constructor_NullInner_ThrowsArgumentNullException()
    {
        var act = () => new RoleGatedExporter<Activity>(null!, _mockLeaderElection.Object);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("inner");
    }

    [Fact]
    public void Constructor_NullLeaderElection_ThrowsArgumentNullException()
    {
        using var inner = new TestExporter();

        var act = () => new RoleGatedExporter<Activity>(inner, null!);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("leaderElection");
    }

    [Fact]
    public void ForceFlush_DelegatesToInner()
    {
        var inner = new TestExporter();
        _mockLeaderElection.Setup(l => l.IsLeader).Returns(true);
        using var sut = new RoleGatedExporter<Activity>(inner, _mockLeaderElection.Object);

        sut.ForceFlush();

        inner.ForceFlushCalled.Should().BeTrue();
    }

    [Fact]
    public void Shutdown_DelegatesToInner()
    {
        var inner = new TestExporter();
        _mockLeaderElection.Setup(l => l.IsLeader).Returns(true);
        using var sut = new RoleGatedExporter<Activity>(inner, _mockLeaderElection.Object);

        sut.Shutdown();

        inner.ShutdownCalled.Should().BeTrue();
    }

    [Fact]
    public void Dispose_DisposesInner()
    {
        var inner = new TestExporter();
        _mockLeaderElection.Setup(l => l.IsLeader).Returns(true);
        var sut = new RoleGatedExporter<Activity>(inner, _mockLeaderElection.Object);

        sut.Dispose();

        inner.DisposeCalled.Should().BeTrue();
    }
}
