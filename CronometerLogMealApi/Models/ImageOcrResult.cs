using System.Text.Json.Serialization;

namespace CronometerLogMealApi.Models;

/// <summary>
/// Result of OCR processing on a handwritten meal log image.
/// </summary>
public class ImageOcrResult
{
    /// <summary>
    /// Full transcription of all visible text.
    /// </summary>
    [JsonPropertyName("transcription")]
    public string Transcription { get; set; } = string.Empty;

    /// <summary>
    /// Date extracted from the image (format: yyyy-MM-dd).
    /// </summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>
    /// List of people identified in the image (e.g., "Luis", "Lioska").
    /// </summary>
    [JsonPropertyName("people")]
    public List<string> People { get; set; } = new();

    /// <summary>
    /// Types of meals identified (e.g., "Desayuno", "Almuerzo").
    /// </summary>
    [JsonPropertyName("mealTypes")]
    public List<string> MealTypes { get; set; } = new();

    /// <summary>
    /// Structured meal sections extracted from the image.
    /// </summary>
    [JsonPropertyName("sections")]
    public List<ImageMealSection> Sections { get; set; } = new();

    /// <summary>
    /// Items that were difficult to read and may need clarification.
    /// </summary>
    [JsonPropertyName("uncertainItems")]
    public List<UncertainItem> UncertainItems { get; set; } = new();

    /// <summary>
    /// Whether clarification is needed from the user.
    /// </summary>
    [JsonPropertyName("needsClarification")]
    public bool NeedsClarification { get; set; }

    /// <summary>
    /// Questions to ask the user for clarification.
    /// </summary>
    [JsonPropertyName("clarificationQuestions")]
    public List<string> ClarificationQuestions { get; set; } = new();

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// A section of meal data for a specific person and meal type.
/// </summary>
public class ImageMealSection
{
    [JsonPropertyName("person")]
    public string Person { get; set; } = string.Empty;

    [JsonPropertyName("mealType")]
    public string MealType { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<ImageMealItem> Items { get; set; } = new();
}

/// <summary>
/// A food item extracted from the image.
/// </summary>
public class ImageMealItem
{
    /// <summary>
    /// The raw text as read from the image.
    /// </summary>
    [JsonPropertyName("raw")]
    public string Raw { get; set; } = string.Empty;

    /// <summary>
    /// Parsed quantity.
    /// </summary>
    [JsonPropertyName("quantity")]
    public double? Quantity { get; set; }

    /// <summary>
    /// Parsed unit (g, cup, tbsp, etc.).
    /// </summary>
    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    /// <summary>
    /// Parsed food name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// An item that was difficult to read and may need clarification.
/// </summary>
public class UncertainItem
{
    /// <summary>
    /// What was originally read (possibly incorrect).
    /// </summary>
    [JsonPropertyName("original")]
    public string Original { get; set; } = string.Empty;

    /// <summary>
    /// Suggested correction.
    /// </summary>
    [JsonPropertyName("suggestion")]
    public string? Suggestion { get; set; }

    /// <summary>
    /// Question to ask the user about this item.
    /// </summary>
    [JsonPropertyName("question")]
    public string? Question { get; set; }
}
