namespace CronometerLogMealApi.Helpers;

/// <summary>
/// Utility class for cleaning and normalizing text.
/// </summary>
public static class TextNormalizer
{
    /// <summary>
    /// Removes markdown formatting from text.
    /// </summary>
    public static string RemoveMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        if (text.StartsWith("```json"))
            text = text.Substring("```json".Length);
        if (text.StartsWith("```"))
            text = text.Substring("```".Length);
        if (text.EndsWith("```"))
            text = text.Substring(0, text.Length - "```".Length);

        return text
            .Replace("**", "")
            .Replace("__", "")
            .Replace("*", "")
            .Replace("_", "")
            .Replace("`", "")
            .Trim();
    }

    /// <summary>
    /// Normalizes a search query or food name for better matching.
    /// </summary>
    public static string NormalizeSearchQuery(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var normalized = input.Trim().ToLowerInvariant();

        // Remove punctuation
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[,\.\-_]", " ");

        // Remove common noise words
        var noiseWords = new[] { "de", "con", "the", "a", "an", "and", "y", "or", "o", "en", "in" };
        foreach (var word in noiseWords)
        {
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized, 
                $@"\b{word}\b", 
                " ", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // Remove extra whitespace
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();

        return normalized;
    }

    /// <summary>
    /// Normalizes a term for consistent matching (lowercase, trimmed).
    /// </summary>
    public static string NormalizeTerm(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return input.Trim().ToLowerInvariant();
    }
}
