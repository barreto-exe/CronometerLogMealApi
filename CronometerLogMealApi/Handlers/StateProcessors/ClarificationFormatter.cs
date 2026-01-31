using CronometerLogMealApi.Models;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Helper class for formatting clarification questions.
/// </summary>
public static class ClarificationFormatter
{
    /// <summary>
    /// Formats a list of clarification items into a user-friendly message.
    /// </summary>
    public static string Format(List<ClarificationItem> clarifications)
    {
        if (clarifications.Count == 0) return string.Empty;

        if (clarifications.Count == 1)
        {
            return clarifications[0].Question;
        }

        var questions = clarifications.Select((c, i) => $"{i + 1}. {c.Question}");
        return string.Join("\n", questions);
    }
}
