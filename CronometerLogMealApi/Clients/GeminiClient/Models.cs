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
    public string? Text { get; set; }
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
