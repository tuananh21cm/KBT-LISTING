namespace Shared.Constants;

/// <summary>
/// Setting keys for order handling fees (BuyTrack scan / ThankCard).
/// </summary>
public static class OrderHandlingSettingKeys
{
    /// <summary>USD fee to buy USPS scan track. Default 0.5.</summary>
    public const string BuyTrack = "order.handling.buy_track";

    /// <summary>USD fee to buy thank-you card for warehouse-track orders. Default 1.</summary>
    public const string ThankCard = "order.handling.thank_card";
}
