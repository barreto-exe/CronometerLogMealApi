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

    /// <summary>
    /// Processes a meal description with user preferences included in the prompt context.
    /// </summary>
    /// <param name="text">The meal description text.</param>
    /// <param name="chatId">The chat ID for session logging.</param>
    /// <param name="userPreferences">Pre-formatted user preferences string to include in the prompt.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<MealProcessingResult> ProcessMealDescriptionAsync(string text, string? chatId, string? userPreferences, CancellationToken ct);
}
