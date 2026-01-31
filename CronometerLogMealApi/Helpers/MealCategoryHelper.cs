namespace CronometerLogMealApi.Helpers;

/// <summary>
/// Helper for determining meal order based on category.
/// </summary>
public static class MealCategoryHelper
{
    /// <summary>
    /// Gets the Cronometer order value for a meal category.
    /// </summary>
    public static int GetOrderForCategory(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "breakfast" => 65537,
            "lunch" => 131073,
            "dinner" => 196609,
            "snacks" => 262145,
            _ => 1
        };
    }

    /// <summary>
    /// Normalizes a category string to standard format.
    /// </summary>
    public static string NormalizeCategory(string category)
    {
        return category.ToUpperInvariant() switch
        {
            "DESAYUNO" => "BREAKFAST",
            "ALMUERZO" => "LUNCH",
            "CENA" => "DINNER",
            "MERIENDA" => "SNACKS",
            "SNACK" => "SNACKS",
            _ => category.ToUpperInvariant()
        };
    }
}
