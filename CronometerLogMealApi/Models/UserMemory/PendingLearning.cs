namespace CronometerLogMealApi.Models.UserMemory;

/// <summary>
/// Represents a pending learning item that will be confirmed after /save.
/// This is stored in memory (not Firestore) until confirmed.
/// </summary>
public class PendingLearning
{
    /// <summary>
    /// The original term the user typed.
    /// </summary>
    public string OriginalTerm { get; set; } = string.Empty;

    /// <summary>
    /// The resolved food name from Cronometer.
    /// </summary>
    public string ResolvedFoodName { get; set; } = string.Empty;

    /// <summary>
    /// The resolved food ID in Cronometer.
    /// </summary>
    public long ResolvedFoodId { get; set; }

    /// <summary>
    /// The source tab where this food was found.
    /// </summary>
    public string SourceTab { get; set; } = string.Empty;

    /// <summary>
    /// Whether this learning is for a food alias.
    /// </summary>
    public bool IsFoodAlias { get; set; } = true;

    /// <summary>
    /// If this is for measure preference, the preferred unit.
    /// </summary>
    public string? PreferredUnit { get; set; }

    /// <summary>
    /// If this is for measure preference, the preferred quantity.
    /// </summary>
    public double? PreferredQuantity { get; set; }
}
