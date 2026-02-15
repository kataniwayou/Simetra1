using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Simetra.Configuration;
using Simetra.Models;
using Simetra.Pipeline;

namespace Simetra.Tests.Pipeline;

public class ProcessingCoordinatorTests
{
    private readonly Mock<IMetricFactory> _mockMetrics = new();
    private readonly Mock<IStateVectorService> _mockStateVector = new();
    private readonly ProcessingCoordinator _sut;

    public ProcessingCoordinatorTests()
    {
        _sut = new ProcessingCoordinator(
            _mockMetrics.Object,
            _mockStateVector.Object,
            Mock.Of<ILogger<ProcessingCoordinator>>());
    }

    private static ExtractionResult MakeResult(MetricPollSource source)
    {
        var definition = new PollDefinitionDto(
            "test_metric",
            MetricType.Gauge,
            Array.Empty<OidEntryDto>().ToList().AsReadOnly(),
            30,
            source);

        return new ExtractionResult { Definition = definition };
    }

    private static DeviceInfo MakeDevice()
    {
        return new DeviceInfo(
            "dev1",
            "10.0.0.1",
            "router",
            Array.Empty<PollDefinitionDto>().ToList().AsReadOnly());
    }

    [Fact]
    public void Process_SourceModule_CallsBothBranches()
    {
        var result = MakeResult(MetricPollSource.Module);
        var device = MakeDevice();

        _sut.Process(result, device, "corr-1");

        _mockMetrics.Verify(m => m.RecordMetrics(result, device), Times.Once);
        _mockStateVector.Verify(
            sv => sv.Update("dev1", "test_metric", result, "corr-1"), Times.Once);
    }

    [Fact]
    public void Process_SourceConfiguration_CallsMetricsOnly()
    {
        var result = MakeResult(MetricPollSource.Configuration);
        var device = MakeDevice();

        _sut.Process(result, device, "corr-1");

        _mockMetrics.Verify(m => m.RecordMetrics(result, device), Times.Once);
        _mockStateVector.Verify(
            sv => sv.Update(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ExtractionResult>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public void Process_BranchAFailure_DoesNotBlockBranchB()
    {
        var result = MakeResult(MetricPollSource.Module);
        var device = MakeDevice();

        _mockMetrics
            .Setup(m => m.RecordMetrics(It.IsAny<ExtractionResult>(), It.IsAny<DeviceInfo>()))
            .Throws(new InvalidOperationException("Branch A boom"));

        _sut.Process(result, device, "corr-1");

        _mockStateVector.Verify(
            sv => sv.Update("dev1", "test_metric", result, "corr-1"), Times.Once);
    }

    [Fact]
    public void Process_BranchBFailure_DoesNotBlockBranchA()
    {
        var result = MakeResult(MetricPollSource.Module);
        var device = MakeDevice();

        _mockStateVector
            .Setup(sv => sv.Update(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ExtractionResult>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("Branch B boom"));

        _sut.Process(result, device, "corr-1");

        _mockMetrics.Verify(m => m.RecordMetrics(result, device), Times.Once);
    }
}
