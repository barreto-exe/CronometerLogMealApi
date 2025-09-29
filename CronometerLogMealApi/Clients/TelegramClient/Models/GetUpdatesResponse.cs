using System.Text.Json.Serialization;

namespace CronometerLogMealApi.Clients.TelegramClient.Models;

public class GetUpdatesResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("result")] public List<TelegramUpdate> Result { get; set; } = new();
}

public class TelegramUpdate
{
    [JsonPropertyName("update_id")] public long UpdateId { get; set; }
    [JsonPropertyName("message")] public TelegramMessage? Message { get; set; }
    [JsonPropertyName("edited_message")] public TelegramMessage? EditedMessage { get; set; }
    // Other update types omitted for brevity
}
