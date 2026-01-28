using Tesseract;

namespace CronometerLogMealApi.Services;

/// <summary>
/// OCR service using Tesseract.NET for local image text extraction.
/// Requires tessdata folder with language files (spa.traineddata for Spanish).
/// </summary>
public class TesseractOcrService : IDisposable
{
    private readonly ILogger<TesseractOcrService> _logger;
    private readonly string _tessDataPath;
    private TesseractEngine? _engine;
    private bool _initialized;
    private readonly object _lock = new();

    public TesseractOcrService(ILogger<TesseractOcrService> logger, IConfiguration configuration)
    {
        _logger = logger;
        // Default to tessdata folder in app directory, can be configured
        _tessDataPath = configuration["Tesseract:TessDataPath"] ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
    }

    /// <summary>
    /// Performs OCR on an image and returns the extracted text.
    /// </summary>
    /// <param name="imageBytes">The image bytes (JPEG, PNG, etc.)</param>
    /// <param name="languageCode">Tesseract language code (e.g., "spa" for Spanish, "eng" for English)</param>
    /// <returns>Extracted text from the image</returns>
    public async Task<string> ExtractTextAsync(byte[] imageBytes, string languageCode = "spa")
    {
        return await Task.Run(() => ExtractText(imageBytes, languageCode));
    }

    private string ExtractText(byte[] imageBytes, string languageCode)
    {
        try
        {
            EnsureEngineInitialized(languageCode);
            
            if (_engine == null)
            {
                _logger.LogError("Tesseract engine not initialized");
                return string.Empty;
            }

            // Load image directly from memory bytes
            using var pix = Pix.LoadFromMemory(imageBytes);
            using var page = _engine.Process(pix);
            
            var text = page.GetText();
            var confidence = page.GetMeanConfidence();
            
            _logger.LogInformation("Tesseract OCR extracted text with {Confidence:P0} confidence, {CharCount} chars", 
                confidence, text?.Length ?? 0);
            
            return text?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing Tesseract OCR");
            return string.Empty;
        }
    }

    private void EnsureEngineInitialized(string languageCode)
    {
        if (_initialized && _engine != null)
            return;

        lock (_lock)
        {
            if (_initialized)
                return;

            try
            {
                // Ensure tessdata directory exists
                if (!Directory.Exists(_tessDataPath))
                {
                    _logger.LogWarning("TessData path does not exist: {Path}. Creating directory.", _tessDataPath);
                    Directory.CreateDirectory(_tessDataPath);
                }

                // Check if language file exists
                var langFile = Path.Combine(_tessDataPath, $"{languageCode}.traineddata");
                if (!File.Exists(langFile))
                {
                    _logger.LogError("Language file not found: {LangFile}. Download from https://github.com/tesseract-ocr/tessdata", langFile);
                    throw new FileNotFoundException($"Tesseract language file not found: {langFile}");
                }

                _engine = new TesseractEngine(_tessDataPath, languageCode, EngineMode.Default);
                _initialized = true;
                
                _logger.LogInformation("Tesseract engine initialized with language: {Lang} from {Path}", 
                    languageCode, _tessDataPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Tesseract engine");
                throw;
            }
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
        _initialized = false;
    }
}
