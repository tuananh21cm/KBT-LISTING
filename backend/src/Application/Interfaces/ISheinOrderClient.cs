using Application.DTOs.Shein;

namespace Application.Interfaces;

/// <summary>
/// Calls the SHEIN storefront order APIs from the server using a session captured by the fulfil
/// extension. Replays the captured security headers plus cookie; the browser is not involved.
/// </summary>
public interface ISheinOrderClient
{
    /// <summary>Fetches one page of <c>/bff-api/order/list</c> as raw JSON.</summary>
    Task<SheinApiResult> FetchOrderListPageAsync(
        SheinSessionContext session,
        int page,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches <c>get_order_info</c> for one billno as raw JSON.</summary>
    Task<SheinApiResult> FetchOrderDetailAsync(
        SheinSessionContext session,
        string billno,
        CancellationToken cancellationToken = default);
}
