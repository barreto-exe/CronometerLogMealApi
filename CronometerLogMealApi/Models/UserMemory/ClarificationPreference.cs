using Google.Cloud.Firestore;

namespace CronometerLogMealApi.Models.UserMemory;

/// <summary>
/// Represents a saved clarification preference.
/// When a user consistently gives the same answer to a clarification question,
/// we remember it to avoid asking again.
/// Example: "huevos" + size question -> "grande" (user always says large eggs)
/// </summary>
[FirestoreData]
public class ClarificationPreference
{
    /// <summary>
    /// Firestore document ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The user ID (Telegram chat ID).
    /// </summary>
    [FirestoreProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The original food term in the user's language (e.g., "huevos", not "eggs").
    /// </summary>
    [FirestoreProperty("foodTerm")]
    public string FoodTerm { get; set; } = string.Empty;

    /// <summary>
    /// The type of clarification this preference answers.
    /// E.g., "MISSING_SIZE", "MISSING_WEIGHT", "AMBIGUOUS_UNIT"
    /// </summary>
    [FirestoreProperty("clarificationType")]
    public string ClarificationType { get; set; } = string.Empty;

    /// <summary>
    /// The default answer/value for this clarification.
    /// E.g., "grande", "200g", "taza"
    /// </summary>
    [FirestoreProperty("defaultAnswer")]
    public string DefaultAnswer { get; set; } = string.Empty;

    /// <summary>
    /// How many times this pattern has been observed.
    /// We only save as preference after 2+ occurrences.
    /// </summary>
    [FirestoreProperty("occurrenceCount")]
    public int OccurrenceCount { get; set; } = 1;

    /// <summary>
    /// Whether this preference has been confirmed (count >= 2).
    /// </summary>
    [FirestoreProperty("isConfirmed")]
    public bool IsConfirmed { get; set; } = false;

    /// <summary>
    /// When this preference was first created.
    /// </summary>
    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this preference was last used.
    /// </summary>
    [FirestoreProperty("lastUsedAt")]
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tracks a pending clarification pattern (before it becomes a confirmed preference).
/// Stored in-memory per session, synced to Firestore after patterns repeat.
/// </summary>
public class PendingClarificationPattern
{
    /// <summary>
    /// The original food term from user input (in their language).
    /// </summary>
    public string OriginalFoodTerm { get; set; } = string.Empty;

    /// <summary>
    /// The clarification type that was asked.
    /// </summary>
    public string ClarificationType { get; set; } = string.Empty;

    /// <summary>
    /// The question that was asked.
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// The user's answer.
    /// </summary>
    public string Answer { get; set; } = string.Empty;
}
