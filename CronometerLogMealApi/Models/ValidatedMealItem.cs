namespace CronometerLogMealApi.Models;

/// <summary>
/// Represents a meal item that has been validated against Cronometer's database.
/// Contains the exact names and IDs from Cronometer.
/// </summary>
public class ValidatedMealItem
{
    /// <summary>
    /// Original name requested by the user.
    /// </summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>
    /// Exact name of the food as it appears in Cronometer's database.
    /// </summary>
    public string FoodName { get; set; } = string.Empty;

    /// <summary>
    /// Food ID in Cronometer's database.
    /// </summary>
    public long FoodId { get; set; }

    /// <summary>
    /// Quantity of the food item.
    /// </summary>
    public double Quantity { get; set; }

    /// <summary>
    /// Name of the measure (e.g., "Large", "g", "tsp").
    /// </summary>
    public string MeasureName { get; set; } = string.Empty;

    /// <summary>
    /// Measure ID in Cronometer's database.
    /// </summary>
    public long MeasureId { get; set; }

    /// <summary>
    /// Grams value for the measure (used for calculations).
    /// </summary>
    public double MeasureGrams { get; set; }
}
