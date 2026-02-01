using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Models.UserMemory;
using System.Text;

namespace CronometerLogMealApi.Helpers;

/// <summary>
/// Helper class for loading and formatting user preferences for LLM prompts.
/// </summary>
public static class UserPreferencesHelper
{
    /// <summary>
    /// Loads all user preferences from memory service and formats them for the LLM prompt.
    /// </summary>
    public static async Task<string?> LoadFormattedPreferencesAsync(
        IUserMemoryService? memoryService,
        string chatId,
        CancellationToken ct)
    {
        if (memoryService == null)
            return null;

        // Fetch all preferences in parallel
        var aliasesTask = memoryService.GetUserAliasesAsync(chatId, ct);
        var clarificationPrefsTask = memoryService.GetUserClarificationPreferencesAsync(chatId, ct);
        var measurePrefsTask = memoryService.GetUserMeasurePreferencesAsync(chatId, ct);

        await Task.WhenAll(aliasesTask, clarificationPrefsTask, measurePrefsTask);

        var aliases = await aliasesTask;
        var clarificationPrefs = await clarificationPrefsTask;
        var measurePrefs = await measurePrefsTask;

        return FormatPreferencesForPrompt(aliases, clarificationPrefs, measurePrefs);
    }

    /// <summary>
    /// Formats all user preferences into a string for the LLM prompt.
    /// </summary>
    public static string FormatPreferencesForPrompt(
        List<FoodAlias>? aliases,
        List<ClarificationPreference>? clarificationPrefs,
        List<MeasurePreference>? measurePrefs)
    {
        var sb = new StringBuilder();
        var hasPreferences = false;

        // Food Aliases section
        if (aliases != null && aliases.Count > 0)
        {
            hasPreferences = true;
            sb.AppendLine("FOOD ALIASES (use the resolved name when the user mentions the input term):");
            foreach (var alias in aliases.Take(20)) // Limit to avoid prompt bloat
            {
                sb.AppendLine($"  - \"{alias.InputTerm}\" → \"{alias.ResolvedFoodName}\"");
            }
            sb.AppendLine();
        }

        // Clarification Preferences section
        if (clarificationPrefs != null && clarificationPrefs.Count > 0)
        {
            hasPreferences = true;
            sb.AppendLine("CLARIFICATION PREFERENCES (apply these defaults, do NOT ask again):");
            foreach (var pref in clarificationPrefs.Take(20))
            {
                var clarificationTypeDescription = pref.ClarificationType switch
                {
                    "MISSING_SIZE" or "MissingSize" => "size",
                    "MISSING_WEIGHT" or "MissingWeight" => "weight",
                    "AMBIGUOUS_UNIT" or "AmbiguousUnit" => "unit type",
                    "UNCLEAR_FOOD" or "FoodNotFound" => "food type",
                    _ => "default"
                };
                sb.AppendLine($"  - When \"{pref.FoodTerm}\" {clarificationTypeDescription} is unclear → use \"{pref.DefaultAnswer}\"");
            }
            sb.AppendLine();
        }

        // Measure Preferences section
        if (measurePrefs != null && measurePrefs.Count > 0)
        {
            hasPreferences = true;
            sb.AppendLine("MEASURE PREFERENCES (use these default units/quantities when not specified):");
            foreach (var pref in measurePrefs.Take(20))
            {
                var quantityStr = pref.PreferredQuantity.HasValue ? $"{pref.PreferredQuantity.Value} " : "";
                sb.AppendLine($"  - \"{pref.FoodNamePattern}\" → default: {quantityStr}{pref.PreferredUnit}");
            }
            sb.AppendLine();
        }

        return hasPreferences ? sb.ToString().TrimEnd() : "No saved preferences for this user.";
    }
}
