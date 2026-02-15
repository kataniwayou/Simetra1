namespace Simetra.Pipeline;

/// <summary>
/// Simple correlation service that generates a single correlation ID at construction
/// and returns it for the entire service lifetime. Phase 6 replaces this registration
/// with the real rotating correlation implementation.
/// </summary>
public sealed class StartupCorrelationService : ICorrelationService
{
    private readonly string _correlationId;

    /// <summary>
    /// Initializes a new instance with a stable startup correlation ID.
    /// </summary>
    public StartupCorrelationService()
    {
        _correlationId = $"startup-{Guid.NewGuid():N}";
    }

    /// <inheritdoc />
    public string CurrentCorrelationId => _correlationId;
}
