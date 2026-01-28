using CronometerLogMealApi.Requests;

namespace CronometerLogMealApi.Models;

/// <summary>
/// Result of processing a meal description through the LLM.
/// </summary>
public class MealProcessingResult
{
    /// <summary>
    /// Whether the processing was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Whether clarification is needed before proceeding.
    /// </summary>
    public bool NeedsClarification { get; set; }

    /// <summary>
    /// The parsed meal request (may be partial if clarification is needed).
    /// </summary>
    public LogMealRequest? MealRequest { get; set; }

    /// <summary>
    /// List of clarifications needed from the user.
    /// </summary>
    public List<ClarificationItem> Clarifications { get; set; } = new();

    /// <summary>
    /// Error message if processing failed completely.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a successful result with no clarifications needed.
    /// </summary>
    public static MealProcessingResult Successful(LogMealRequest request) => new()
    {
        Success = true,
        NeedsClarification = false,
        MealRequest = request
    };

    /// <summary>
    /// Creates a result indicating clarification is needed.
    /// </summary>
    public static MealProcessingResult RequiresClarification(
        LogMealRequest? partialRequest,
        List<ClarificationItem> clarifications) => new()
    {
        Success = true,
        NeedsClarification = true,
        MealRequest = partialRequest,
        Clarifications = clarifications
    };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static MealProcessingResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
