using Google.Cloud.Firestore;

namespace CronometerLogMealApi.Models.UserMemory;

/// <summary>
/// Represents a user's preferred measure for a specific food or food pattern.
/// </summary>
[FirestoreData]
public class MeasurePreference
{
    /// <summary>
    /// Unique identifier for the preference.
    /// </summary>
    [FirestoreProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The user's Telegram chat ID.
    /// </summary>
    [FirestoreProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The food name pattern this preference applies to (can be exact or partial).
    /// e.g., "Chicken Breast", "eggs"
    /// </summary>
    [FirestoreProperty("foodNamePattern")]
    public string FoodNamePattern { get; set; } = string.Empty;

    /// <summary>
    /// The preferred unit/measure name.
    /// e.g., "g", "large", "cup"
    /// </summary>
    [FirestoreProperty("preferredUnit")]
    public string PreferredUnit { get; set; } = string.Empty;

    /// <summary>
    /// The preferred default quantity when not specified.
    /// </summary>
    [FirestoreProperty("preferredQuantity")]
    public double? PreferredQuantity { get; set; }

    /// <summary>
    /// Number of times this preference has been used.
    /// </summary>
    [FirestoreProperty("useCount")]
    public int UseCount { get; set; } = 1;

    /// <summary>
    /// When this preference was last used.
    /// </summary>
    [FirestoreProperty("lastUsedAt")]
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this preference was created.
    /// </summary>
    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this preference is currently active.
    /// </summary>
    [FirestoreProperty("isActive")]
    public bool IsActive { get; set; } = true;
}
