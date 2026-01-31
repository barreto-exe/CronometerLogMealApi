using CronometerLogMealApi.Models;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Helper class for building conversation context from message history.
/// </summary>
public static class ConversationContextBuilder
{
    /// <summary>
    /// Builds a comprehensive context string from conversation history.
    /// </summary>
    public static string Build(List<ConversationMessage> history)
    {
        var sb = new System.Text.StringBuilder();
        bool isFirst = true;

        for (int i = 0; i < history.Count; i++)
        {
            var msg = history[i];

            if (msg.Role == "user")
            {
                if (isFirst)
                {
                    sb.Append($"Meal description: {msg.Content}");
                    isFirst = false;
                }
                else
                {
                    if (i > 0 && history[i - 1].Role == "assistant")
                    {
                        var question = history[i - 1].Content;
                        sb.Append($"\nClarification question: {question}");
                        sb.Append($"\nUser answered: {msg.Content}");
                    }
                    else
                    {
                        sb.Append($"\nAdditional info: {msg.Content}");
                    }
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the original meal description from conversation history.
    /// </summary>
    public static string GetOriginalDescription(List<ConversationMessage> history)
    {
        var firstUserMessage = history.FirstOrDefault(m => m.Role == "user");
        if (firstUserMessage != null)
        {
            return firstUserMessage.Content;
        }

        return string.Join(" ", history.Where(m => m.Role == "user").Select(m => m.Content));
    }
}
