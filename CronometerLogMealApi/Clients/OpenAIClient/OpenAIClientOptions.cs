namespace CronometerLogMealApi.Clients.OpenAIClient;

public class OpenAIClientOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public string? VisionModel { get; set; } = "gpt-4o"; // Model for image analysis
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
}

