using System.Text.Json.Serialization;

namespace CronometerLogMealApi.Clients.TelegramClient.Requests;

public class GetUpdatesRequest
{
    // Identifier of the first update to be returned. Must be greater by one than the highest among the identifiers of previously received updates.
    [JsonPropertyName("offset")]
    public long? Offset { get; set; }

    // Limits the number of updates to be retrieved. Values between 1—100 are accepted. Defaults to 100.
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    // Timeout in seconds for long polling. Defaults to 0, i.e., usual short polling.
    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }

    // A JSON-serialized list of the update types you want your bot to receive. For now we omit for simplicity.
    [JsonPropertyName("allowed_updates")]
    public string[]? AllowedUpdates { get; set; }
}
