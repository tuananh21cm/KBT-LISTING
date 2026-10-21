namespace Application.Interfaces;

/// <summary>
/// Process-wide serial gate for USPS scan push (avoids concurrent double-charge).
/// Single-instance only — multi-instance deployments should upgrade to pg_advisory_xact_lock.
/// </summary>
public interface IUspsScanPushGate
{
    /// <summary>Acquires the exclusive push lock.</summary>
    Task WaitAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases the exclusive push lock.</summary>
    void Release();
}
