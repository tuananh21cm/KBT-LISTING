using Application.DTOs.Order;

namespace Application.Interfaces;

/// <summary>
/// Guards the denormalized workflow columns on <c>Orders</c> against drift: recomputes them from
/// the source helpers and reports (optionally repairs) rows where the stored value disagrees.
///
/// Without this the denormalization is a reliability risk — a write path that bypasses the
/// <c>SaveChangesAsync</c> writer (raw SQL, <c>ExecuteUpdate</c>) would leave a wrong tab in place
/// with nothing to notice it.
/// </summary>
public interface IOrderWorkflowSignalsVerifier
{
    /// <summary>
    /// Scans up to <paramref name="maxOrders"/> orders, newest first.
    /// </summary>
    /// <param name="maxOrders">Cap on rows inspected; 0 or less means every order.</param>
    /// <param name="repair">When true, rewrites the columns of the drifted rows.</param>
    /// <param name="cancellationToken">Token huỷ request.</param>
    Task<OrderWorkflowSignalsDriftDto> VerifyAsync(
        int maxOrders,
        bool repair,
        CancellationToken cancellationToken = default);
}
