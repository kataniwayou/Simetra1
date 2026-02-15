using Quartz;

namespace Simetra.Jobs;

/// <summary>
/// Generates a new correlationId and stamps the liveness vector at the configured interval.
/// Stub implementation -- fleshed out by Plan 06-03.
/// </summary>
[DisallowConcurrentExecution]
public sealed class CorrelationJob : IJob
{
    public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
}
