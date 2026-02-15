namespace Simetra.Pipeline;

/// <summary>
/// Provides a correlation ID for linking related pipeline operations across all jobs
/// within a time window. The correlation ID is rotated periodically by the CorrelationJob,
/// with the first ID generated directly on startup before any job fires.
/// </summary>
public interface ICorrelationService
{
    /// <summary>
    /// Gets the current correlation ID. Thread-safe for concurrent readers.
    /// </summary>
    string CurrentCorrelationId { get; }

    /// <summary>
    /// Sets a new correlation ID. Must be called from a single writer at a time --
    /// startup code sets the first ID, then CorrelationJob is the sole writer.
    /// </summary>
    /// <param name="correlationId">The new correlation ID to set.</param>
    void SetCorrelationId(string correlationId);
}
