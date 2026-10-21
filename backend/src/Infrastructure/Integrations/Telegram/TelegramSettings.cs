namespace Infrastructure.Integrations.Telegram;

/// <summary>
/// Telegram Bot credentials from appsettings (rarely changed; not managed via admin Settings UI).
/// </summary>
public class TelegramSettings
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Telegram";

    /// <summary>
    /// Telegram Bot API token (BotFather).
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Fallback chat / group id for order + summary notifications when
    /// <c>telegram.orders_chat_id</c> setting is empty.
    /// </summary>
    public string ChatId { get; set; } = string.Empty;
}
