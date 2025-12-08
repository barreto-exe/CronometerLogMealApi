namespace CronometerLogMealApi.Clients.GeminiClient;

public class GeminiClientOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-pro"; // default
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    
    /// <summary>
    /// Cookies for authentication (e.g. __Secure-1PSID, __Secure-1PSIDTS).
    /// </summary>
    public Dictionary<string, string> Cookies { get; set; } = new();
}
