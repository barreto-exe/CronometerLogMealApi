using CronometerLogMealApi.Clients.TelegramClient.Models;

namespace CronometerLogMealApi.Abstractions;

/// <summary>
/// Abstraction for Telegram messaging operations.
/// </summary>
public interface ITelegramService
{
    /// <summary>
    /// Gets the last processed update ID.
    /// </summary>
    long LastUpdateId { get; }

    /// <summary>
    /// Initializes the service and fetches the latest update ID.
    /// </summary>
    Task InitAsync(CancellationToken ct);

    /// <summary>
    /// Gets pending updates from Telegram.
    /// </summary>
    Task<GetUpdatesResponse?> GetTelegramUpdates(long? offset, CancellationToken ct);

    /// <summary>
    /// Sends a text message to a chat.
    /// </summary>
    Task<SendMessageResponse?> SendMessageAsync(string chatId, string text, string? parseMode, CancellationToken ct);
}
