namespace Simetra.Pipeline;

/// <summary>
/// Provides a correlation ID for linking related pipeline operations.
/// This is a placeholder abstraction; Phase 6 replaces the startup implementation
/// with a rotating correlation job that produces time-windowed IDs.
/// </summary>
public interface ICorrelationService
{
    /// <summary>
    /// Gets the current correlation ID.
    /// </summary>
    string CurrentCorrelationId { get; }
}
