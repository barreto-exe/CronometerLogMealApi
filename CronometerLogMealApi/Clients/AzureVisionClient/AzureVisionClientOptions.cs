namespace CronometerLogMealApi.Clients.AzureVisionClient;

/// <summary>
/// Configuration options for Azure AI Vision Image Analysis.
/// </summary>
public class AzureVisionClientOptions
{
    /// <summary>
    /// Azure Computer Vision endpoint URL (e.g., https://your-resource.cognitiveservices.azure.com/)
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure subscription key for the Computer Vision resource.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
