using Quartz;

namespace Simetra.Jobs;

/// <summary>
/// Sends a loopback SNMP v2c trap to the listener using the heartbeat OID from
/// SimetraModule, proving the scheduler is alive. Stamps liveness vector on completion.
/// Stub implementation -- fleshed out by Plan 06-03.
/// </summary>
[DisallowConcurrentExecution]
public sealed class HeartbeatJob : IJob
{
    public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
}
