using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Microsoft.Extensions.Options;

namespace CronometerLogMealApi.Clients.AzureVisionClient;

/// <summary>
/// Service for performing OCR on images using Azure AI Vision.
/// </summary>
public class AzureVisionService
{
    private readonly ImageAnalysisClient _client;
    private readonly ILogger<AzureVisionService> _logger;

    public AzureVisionService(IOptions<AzureVisionClientOptions> options, ILogger<AzureVisionService> logger)
    {
        var opts = options.Value;
        _client = new ImageAnalysisClient(
            new Uri(opts.Endpoint),
            new AzureKeyCredential(opts.ApiKey));
        _logger = logger;
    }

    /// <summary>
    /// Extracts text from an image using Azure AI Vision OCR.
    /// Supports both printed and handwritten text.
    /// </summary>
    /// <param name="imageData">Image bytes to analyze</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Extracted text concatenated from all lines, or null if no text found</returns>
    public async Task<string?> ExtractTextFromImageAsync(byte[] imageData, CancellationToken ct = default)
    {
        try
        {
            var result = await _client.AnalyzeAsync(
                BinaryData.FromBytes(imageData),
                VisualFeatures.Read,
                new ImageAnalysisOptions { GenderNeutralCaption = true },
                ct);

            if (result?.Value?.Read?.Blocks == null || result.Value.Read.Blocks.Count == 0)
            {
                _logger.LogWarning("No text blocks found in image");
                return null;
            }

            // Extract all lines from all blocks
            var allLines = result.Value.Read.Blocks
                .SelectMany(block => block.Lines)
                .Select(line => line.Text);

            var extractedText = string.Join("\n", allLines);

            _logger.LogInformation("Extracted {LineCount} lines of text from image", 
                result.Value.Read.Blocks.Sum(b => b.Lines.Count));

            return string.IsNullOrWhiteSpace(extractedText) ? null : extractedText;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure Vision API request failed: {Message}", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from image");
            throw;
        }
    }
}
