namespace CronometerLogMealApi.Models.UserMemory;

/// <summary>
/// Represents an alias that was detected in the user's input text.
/// </summary>
public class DetectedAlias
{
    /// <summary>
    /// The alias term that was found in the text.
    /// </summary>
    public string MatchedTerm { get; set; } = string.Empty;

    /// <summary>
    /// The position where the match starts in the original text.
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// The length of the matched term.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// The full FoodAlias object with resolution info.
    /// </summary>
    public FoodAlias Alias { get; set; } = null!;
}
