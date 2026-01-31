using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Clients.AzureVisionClient;

namespace CronometerLogMealApi.Services;

/// <summary>
/// Adapter that wraps AzureVisionService to implement IOcrService.
/// </summary>
public class AzureOcrServiceAdapter : IOcrService
{
    private readonly AzureVisionService _azureVisionService;

    public AzureOcrServiceAdapter(AzureVisionService azureVisionService)
    {
        _azureVisionService = azureVisionService;
    }

    public Task<string?> ExtractTextFromImageAsync(byte[] imageBytes, CancellationToken ct)
    {
        return _azureVisionService.ExtractTextFromImageAsync(imageBytes, ct);
    }
}
