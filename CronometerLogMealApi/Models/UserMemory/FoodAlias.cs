using Google.Cloud.Firestore;

namespace CronometerLogMealApi.Models.UserMemory;

/// <summary>
/// Represents a learned alias mapping from a user's input term to a resolved Cronometer food.
/// </summary>
[FirestoreData]
public class FoodAlias
{
    /// <summary>
    /// Unique identifier for the alias.
    /// </summary>
    [FirestoreProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The user's Telegram chat ID.
    /// </summary>
    [FirestoreProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The input term that the user types (normalized to lowercase).
    /// e.g., "pollo", "queso", "mozzarella emmanuel"
    /// </summary>
    [FirestoreProperty("inputTerm")]
    public string InputTerm { get; set; } = string.Empty;

    /// <summary>
    /// The resolved food name from Cronometer DB.
    /// e.g., "Chicken Breast, Raw", "Emmanuel, Queso Mozzarella"
    /// </summary>
    [FirestoreProperty("resolvedFoodName")]
    public string ResolvedFoodName { get; set; } = string.Empty;

    /// <summary>
    /// The resolved food ID in Cronometer.
    /// </summary>
    [FirestoreProperty("resolvedFoodId")]
    public long ResolvedFoodId { get; set; }

    /// <summary>
    /// The source tab where this food was found (e.g., CUSTOM, FAVOURITES, COMMON_FOODS).
    /// </summary>
    [FirestoreProperty("sourceTab")]
    public string SourceTab { get; set; } = string.Empty;

    /// <summary>
    /// Number of times this alias has been used.
    /// </summary>
    [FirestoreProperty("useCount")]
    public int UseCount { get; set; } = 1;

    /// <summary>
    /// When this alias was last used.
    /// </summary>
    [FirestoreProperty("lastUsedAt")]
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this alias was created.
    /// </summary>
    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this alias is currently active.
    /// </summary>
    [FirestoreProperty("isActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether this alias was created manually by the user (via /preferences).
    /// </summary>
    [FirestoreProperty("isManual")]
    public bool IsManual { get; set; } = false;
}
