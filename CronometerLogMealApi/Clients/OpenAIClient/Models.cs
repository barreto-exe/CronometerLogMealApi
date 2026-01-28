using System.Text.Json.Serialization;

namespace CronometerLogMealApi.Clients.OpenAIClient;

public class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }
}

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    /// <summary>
    /// Content can be a string for simple text, or a List&lt;ContentPart&gt; for multimodal (text + images).
    /// </summary>
    [JsonPropertyName("content")]
    public object Content { get; set; } = string.Empty;
}

/// <summary>
/// Represents a part of multimodal content (text or image).
/// </summary>
public class ContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text"; // "text" or "image_url"

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("image_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImageUrlContent? ImageUrl { get; set; }

    public static ContentPart TextContent(string text) => new() { Type = "text", Text = text };
    
    public static ContentPart ImageContent(byte[] imageBytes, string mimeType = "image/jpeg")
    {
        var base64 = Convert.ToBase64String(imageBytes);
        return new ContentPart
        {
            Type = "image_url",
            ImageUrl = new ImageUrlContent { Url = $"data:{mimeType};base64,{base64}" }
        };
    }
}

/// <summary>
/// Image URL content for vision API.
/// </summary>
public class ImageUrlContent
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; } // "low", "high", or "auto"
}

public class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<ChatChoice> Choices { get; set; } = new();
}

public class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

