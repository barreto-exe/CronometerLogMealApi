using System.Text.Json.Serialization;

namespace CronometerLogMealApi.Clients.TelegramClient.Requests;

public class GetFileRequest
{
    [JsonPropertyName("file_id")] public string FileId { get; set; } = string.Empty;
}
