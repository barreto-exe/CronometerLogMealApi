using CronometerLogMealApi.Models.UserMemory;

namespace CronometerLogMealApi.Abstractions;

/// <summary>
/// Abstraction for user memory (aliases and preferences) operations.
/// </summary>
public interface IUserMemoryService
{
    #region Food Aliases

    /// <summary>
    /// Finds an active alias for the given input term and user.
    /// </summary>
    Task<FoodAlias?> FindAliasAsync(string userId, string inputTerm, CancellationToken ct = default);

    /// <summary>
    /// Gets all active aliases for a user.
    /// </summary>
    Task<List<FoodAlias>> GetUserAliasesAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates an alias for a user.
    /// </summary>
    Task<FoodAlias> SaveAliasAsync(
        string userId,
        string inputTerm,
        string resolvedFoodName,
        long resolvedFoodId,
        string sourceTab,
        bool isManual = false,
        CancellationToken ct = default);

    /// <summary>
    /// Increments the use count for an existing alias.
    /// </summary>
    Task IncrementAliasUsageAsync(string aliasId, CancellationToken ct = default);

    /// <summary>
    /// Deactivates (soft deletes) an alias.
    /// </summary>
    Task DeleteAliasAsync(string aliasId, CancellationToken ct = default);

    /// <summary>
    /// Detects aliases in text.
    /// </summary>
    Task<List<DetectedAlias>> DetectAliasesInTextAsync(string userId, string text, CancellationToken ct = default);

    /// <summary>
    /// Finds a matching detected alias for the given item name.
    /// </summary>
    FoodAlias? FindMatchingDetectedAlias(string itemName, List<DetectedAlias> detectedAliases, string originalInput);

    #endregion

    #region Clarification Preferences

    /// <summary>
    /// Finds a clarification preference.
    /// </summary>
    Task<ClarificationPreference?> FindClarificationPreferenceAsync(
        string userId,
        string term,
        string clarificationType,
        CancellationToken ct = default);

    /// <summary>
    /// Records a clarification pattern for learning.
    /// </summary>
    Task<bool> RecordClarificationPatternAsync(
        string userId,
        string term,
        string clarificationType,
        string answer,
        CancellationToken ct = default);

    #endregion

    #region Sessions

    /// <summary>
    /// Gets all active sessions from storage.
    /// </summary>
    Task<List<CronometerSession>> GetAllActiveSessionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves a session to storage.
    /// </summary>
    Task SaveSessionAsync(
        string telegramChatId,
        long cronometerUserId,
        string sessionKey,
        string email,
        CancellationToken ct = default);

    #endregion
}
