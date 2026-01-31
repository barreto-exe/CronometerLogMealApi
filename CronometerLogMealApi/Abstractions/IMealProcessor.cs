using CronometerLogMealApi.Models;

namespace CronometerLogMealApi.Abstractions;

/// <summary>
/// Abstraction for AI-powered meal description processing.
/// </summary>
public interface IMealProcessor
{
    /// <summary>
    /// Processes a meal description and extracts structured data.
    /// </summary>
    Task<MealProcessingResult> ProcessMealDescriptionAsync(string text, CancellationToken ct);

    /// <summary>
    /// Processes a meal description and extracts structured data, with session logging.
    /// </summary>
    Task<MealProcessingResult> ProcessMealDescriptionAsync(string text, string? chatId, CancellationToken ct);
}
