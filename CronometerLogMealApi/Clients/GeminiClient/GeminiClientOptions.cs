namespace CronometerLogMealApi.Clients.GeminiClient;

public class GeminiClientOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.0-flash"; // default for text
    public string? VisionModel { get; set; } = "gemini-2.0-flash"; // for image analysis
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
}

