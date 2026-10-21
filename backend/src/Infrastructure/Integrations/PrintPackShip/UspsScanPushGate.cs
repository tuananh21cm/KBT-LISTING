using Application.Interfaces;

namespace Infrastructure.Integrations.PrintPackShip;

/// <summary>
/// Singleton SemaphoreSlim(1,1) gate for USPS scan push.
/// Note: multi-instance hosts need pg_advisory_xact_lock instead.
/// </summary>
public sealed class UspsScanPushGate : IUspsScanPushGate, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <inheritdoc />
    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        _lock.WaitAsync(cancellationToken);

    /// <inheritdoc />
    public void Release() => _lock.Release();

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();
}
