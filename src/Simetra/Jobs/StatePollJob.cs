using Quartz;

namespace Simetra.Jobs;

/// <summary>
/// Polls a device for state data (Source=Module) using SNMP GET, extracts results via
/// the generic extractor, and feeds them to the processing pipeline.
/// Stub implementation -- fleshed out by Plan 06-02.
/// </summary>
[DisallowConcurrentExecution]
public sealed class StatePollJob : IJob
{
    public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
}
