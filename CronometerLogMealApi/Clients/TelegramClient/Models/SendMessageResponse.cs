using System.Text.Json.Serialization;

namespace CronometerLogMealApi.Clients.TelegramClient.Models;

public class SendMessageResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("result")] public TelegramMessage? Result { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

public class TelegramMessage
{
    [JsonPropertyName("message_id")] public long MessageId { get; set; }
    [JsonPropertyName("chat")] public TelegramChat? Chat { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
}

public class TelegramChat
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
}
