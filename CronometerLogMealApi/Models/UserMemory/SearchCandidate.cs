using CronometerLogMealApi.Clients.CronometerClient.Models;

namespace CronometerLogMealApi.Models.UserMemory;

/// <summary>
/// Represents a food search candidate with scoring information.
/// Used for presenting multiple search options to the user.
/// </summary>
public class SearchCandidate
{
    /// <summary>
    /// The food from Cronometer.
    /// </summary>
    public Food Food { get; set; } = null!;

    /// <summary>
    /// The source tab where this food was found.
    /// </summary>
    public string SourceTab { get; set; } = string.Empty;

    /// <summary>
    /// The composite score for this candidate.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// The similarity score component.
    /// </summary>
    public double SimilarityScore { get; set; }

    /// <summary>
    /// Display string for showing to user.
    /// </summary>
    public string DisplayText => $"{Food.Name} [{SourceTab}]";
}
