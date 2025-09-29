namespace CronometerLogMealApi.Clients.GeminiClient;

public class GeminiClientOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-pro"; // default
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
}
