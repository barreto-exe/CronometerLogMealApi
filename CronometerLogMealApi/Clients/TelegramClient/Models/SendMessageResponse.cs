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
    [JsonPropertyName("from")] public TelegramUser? From { get; set; }
    [JsonPropertyName("chat")] public TelegramChat? Chat { get; set; }
    [JsonPropertyName("date")] public long? Date { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("photo")] public List<TelegramPhotoSize>? Photo { get; set; }
    [JsonPropertyName("caption")] public string? Caption { get; set; }
}

public class TelegramPhotoSize
{
    [JsonPropertyName("file_id")] public string FileId { get; set; } = string.Empty;
    [JsonPropertyName("file_unique_id")] public string FileUniqueId { get; set; } = string.Empty;
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("file_size")] public int? FileSize { get; set; }
}

public class TelegramChat
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
}

public class TelegramUser
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("is_bot")] public bool IsBot { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("language_code")] public string? LanguageCode { get; set; }
}
