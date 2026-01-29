using System.Text.Json.Serialization;

namespace CronometerLogMealApi.Clients.TelegramClient.Models;

public class GetFileResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("result")] public TelegramFile? Result { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

public class TelegramFile
{
    [JsonPropertyName("file_id")] public string FileId { get; set; } = string.Empty;
    [JsonPropertyName("file_unique_id")] public string FileUniqueId { get; set; } = string.Empty;
    [JsonPropertyName("file_size")] public long? FileSize { get; set; }
    [JsonPropertyName("file_path")] public string? FilePath { get; set; }
}
