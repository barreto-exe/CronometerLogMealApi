using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Models;
using CronometerLogMealApi.Models.UserMemory;
using CronometerLogMealApi.Requests;

namespace CronometerLogMealApi.Abstractions;

/// <summary>
/// Abstraction for Cronometer food logging operations.
/// </summary>
public interface ICronometerService
{
    /// <summary>
    /// Logs a meal to Cronometer.
    /// </summary>
    Task<LogMealResult> LogMealAsync(AuthPayload auth, LogMealRequest request, CancellationToken cancellation = default);

    /// <summary>
    /// Validates meal items against Cronometer's database.
    /// </summary>
    Task<(List<ValidatedMealItem> ValidatedItems, List<string> NotFoundItems)> ValidateMealItemsAsync(
        IEnumerable<MealItem> items,
        AuthPayload auth,
        string? userId = null,
        List<DetectedAlias>? detectedAliases = null,
        string? originalUserInput = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for food with memory support.
    /// </summary>
    Task<FoodSearchResult> GetFoodWithMemoryAsync(
        string query,
        AuthPayload auth,
        string? userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Searches for food and returns candidates.
    /// </summary>
    Task<(long FoodId, string FoodName, string SourceTab, List<SearchCandidate> Candidates)>
        SearchFoodWithCandidatesAsync(string query, AuthPayload auth, CancellationToken cancellationToken);
}

/// <summary>
/// Result of a food search operation.
/// </summary>
public class FoodSearchResult
{
    public long FoodId { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public string SourceTab { get; set; } = string.Empty;
    public bool WasFromAlias { get; set; }
    public string? AliasId { get; set; }
    public List<SearchCandidate> AllCandidates { get; set; } = new();
}
