namespace Application.Services;

/// <summary>
/// Pure rules for when order sync may fire Telegram "new order" notifications.
/// </summary>
public static class OrderSyncTelegramPolicy
{
    /// <summary>
    /// Status sync sets <paramref name="skipNewOrderTelegram"/> so only the light new-orders
    /// loop (and the pending sweep) announce inserts.
    /// </summary>
    public static bool ShouldNotifyNewOrders(bool skipNewOrderTelegram, int newlyInsertedCount) =>
        !skipNewOrderTelegram && newlyInsertedCount > 0;
}
