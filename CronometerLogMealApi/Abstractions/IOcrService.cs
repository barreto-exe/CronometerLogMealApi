namespace CronometerLogMealApi.Abstractions;

/// <summary>
/// Abstraction for OCR text extraction from images.
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Extracts text from an image.
    /// </summary>
    Task<string?> ExtractTextFromImageAsync(byte[] imageBytes, CancellationToken ct);
}
