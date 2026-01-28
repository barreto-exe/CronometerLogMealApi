using System.Text.Json.Serialization;

namespace CronometerLogMealApi.Clients.GeminiClient;

public class GenerateContentRequest
{
    [JsonPropertyName("contents")]
    public List<GeminiContent> Contents { get; set; } = [];
    
    [JsonPropertyName("system_instruction")]
    public GeminiContent? SystemInstruction { get; set; }
}

public class GeminiContent
{
    [JsonPropertyName("role")]
    public string? Role { get; set; } // user|model|system (API uses user/model, system via system_instruction)

    [JsonPropertyName("parts")]
    public List<GeminiPart> Parts { get; set; } = new();
}

public class GeminiPart
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>
    /// Inline image data for Vision requests.
    /// </summary>
    [JsonPropertyName("inline_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiInlineData? InlineData { get; set; }

    public static GeminiPart TextPart(string text) => new() { Text = text };
    
    public static GeminiPart ImagePart(byte[] imageBytes, string mimeType = "image/jpeg")
    {
        return new GeminiPart
        {
            InlineData = new GeminiInlineData
            {
                MimeType = mimeType,
                Data = Convert.ToBase64String(imageBytes)
            }
        };
    }
}

/// <summary>
/// Inline data for images in Gemini Vision requests.
/// </summary>
public class GeminiInlineData
{
    [JsonPropertyName("mime_type")]
    public string MimeType { get; set; } = "image/jpeg";

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty; // Base64 encoded
}

public class GenerateContentResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate> Candidates { get; set; } = [];
}

public class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

