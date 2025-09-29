using System.Text.Json.Serialization;

namespace CronometerLogMealApi.Clients.TelegramClient.Requests;

public class SendMessageRequest
{
    // Required: Chat identifier (unique chat id or '@channelusername')
    [JsonPropertyName("chat_id")]
    public string ChatId { get; set; } = string.Empty;

    // Required: Text of the message to be sent
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    // Optional: Parse mode (Markdown, MarkdownV2, HTML)
    [JsonPropertyName("parse_mode")]
    public string? ParseMode { get; set; }

    // Optional: Disable link previews
    [JsonPropertyName("disable_web_page_preview")]
    public bool? DisableWebPagePreview { get; set; }

    // Optional: Disable notification (silent message)
    [JsonPropertyName("disable_notification")]
    public bool? DisableNotification { get; set; }
}
