namespace CronometerLogMealApi.Models;

/// <summary>
/// Result of attempting to log a meal to Cronometer.
/// </summary>
public class LogMealResult
{
    /// <summary>
    /// Whether the meal was logged successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// List of food item names that were not found in Cronometer's database.
    /// </summary>
    public List<string> NotFoundItems { get; set; } = new();

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static LogMealResult Successful() => new() { Success = true };

    /// <summary>
    /// Creates a failed result with not found items.
    /// </summary>
    public static LogMealResult NotFound(List<string> notFoundItems) => new()
    {
        Success = false,
        NotFoundItems = notFoundItems,
        ErrorMessage = "Algunos alimentos no fueron encontrados en la base de datos."
    };

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    public static LogMealResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
