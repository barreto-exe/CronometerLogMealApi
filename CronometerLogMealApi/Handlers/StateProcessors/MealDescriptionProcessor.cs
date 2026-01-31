using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Models;
using CronometerLogMealApi.Models.UserMemory;
using Microsoft.Extensions.Logging;
using System.Text;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Processes meal description messages from the user.
/// </summary>
public class MealDescriptionProcessor : IStateProcessor
{
    private readonly ITelegramService _telegramService;
    private readonly IMealProcessor _mealProcessor;
    private readonly IUserMemoryService? _memoryService;
    private readonly IMealValidationOrchestrator _validationOrchestrator;
    private readonly ILogger<MealDescriptionProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingMealDescription;

    public MealDescriptionProcessor(
        ITelegramService telegramService,
        IMealProcessor mealProcessor,
        IMealValidationOrchestrator validationOrchestrator,
        ILogger<MealDescriptionProcessor> logger,
        IUserMemoryService? memoryService = null)
    {
        _telegramService = telegramService;
        _mealProcessor = mealProcessor;
        _validationOrchestrator = validationOrchestrator;
        _logger = logger;
        _memoryService = memoryService;
    }

    public async Task ProcessAsync(StateContext context, CancellationToken ct)
    {
        var conversation = context.Conversation;
        var text = context.Text;

        // Store original description
        conversation.OriginalDescription = text;
        conversation.MessageHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = text,
            Timestamp = DateTime.UtcNow
        });

        conversation.State = ConversationState.Processing;
        await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Meal.ProcessingMessage, null, ct);

        try
        {
            // Fetch all user preferences for the LLM prompt
            string? userPreferences = null;
            List<FoodAlias>? aliases = null;
            List<ClarificationPreference>? clarificationPrefs = null;
            List<MeasurePreference>? measurePrefs = null;

            if (_memoryService != null)
            {
                // Fetch all preferences in parallel
                var aliasesTask = _memoryService.GetUserAliasesAsync(context.ChatId, ct);
                var clarificationPrefsTask = _memoryService.GetUserClarificationPreferencesAsync(context.ChatId, ct);
                var measurePrefsTask = _memoryService.GetUserMeasurePreferencesAsync(context.ChatId, ct);

                await Task.WhenAll(aliasesTask, clarificationPrefsTask, measurePrefsTask);

                aliases = await aliasesTask;
                clarificationPrefs = await clarificationPrefsTask;
                measurePrefs = await measurePrefsTask;

                userPreferences = FormatPreferencesForPrompt(aliases, clarificationPrefs, measurePrefs);
                _logger.LogInformation("Formatted user preferences for LLM: {Preferences}", userPreferences);
            }

            // Detect and replace aliases before sending to LLM
            var textForLlm = text;
            if (_memoryService != null && aliases != null)
            {
                var detectedAliases = await _memoryService.DetectAliasesInTextAsync(context.ChatId, text, ct);
                conversation.DetectedAliases = detectedAliases;

                if (detectedAliases.Count > 0)
                {
                    _logger.LogInformation("Detected {Count} aliases in user input", detectedAliases.Count);
                    textForLlm = ReplaceAliasesInText(text, detectedAliases);
                    _logger.LogInformation("Text after alias replacement: '{Text}'", textForLlm);
                }
            }

            // Process with LLM (now with user preferences)
            var result = await _mealProcessor.ProcessMealDescriptionAsync(textForLlm, context.ChatId, userPreferences, ct);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                conversation.State = ConversationState.AwaitingMealDescription;
                await _telegramService.SendMessageAsync(context.ChatId,
                    TelegramMessages.Meal.FormatDescriptionError(result.ErrorMessage), null, ct);
                return;
            }

            if (result.NeedsClarification && result.Clarifications.Count > 0)
            {
                await HandleClarificationNeeded(context, result, text, ct);
                return;
            }

            // No clarification needed, proceed to validation
            await _validationOrchestrator.AttemptMealLoggingAsync(context.ChatId, context.UserInfo, result.MealRequest!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing meal description for chatId {ChatId}", context.ChatId);
            conversation.State = ConversationState.AwaitingMealDescription;
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Meal.ProcessingError, null, ct);
        }
    }

    private async Task HandleClarificationNeeded(StateContext context, MealProcessingResult result, string originalText, CancellationToken ct)
    {
        var conversation = context.Conversation;
        var remainingClarifications = new List<ClarificationItem>();
        var autoAppliedAnswers = new List<string>();

        // Check for saved preferences
        if (_memoryService != null)
        {
            foreach (var clarification in result.Clarifications)
            {
                var termToCheck = !string.IsNullOrEmpty(clarification.OriginalTerm)
                    ? clarification.OriginalTerm
                    : clarification.ItemName;

                var preference = await _memoryService.FindClarificationPreferenceAsync(
                    context.ChatId, termToCheck, clarification.Type.ToString(), ct);

                if (preference != null)
                {
                    autoAppliedAnswers.Add($"{termToCheck} -> {preference.DefaultAnswer}");
                    conversation.MessageHistory.Add(new ConversationMessage
                    {
                        Role = "user",
                        Content = preference.DefaultAnswer,
                        Timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    remainingClarifications.Add(clarification);
                }
            }
        }
        else
        {
            remainingClarifications = result.Clarifications;
        }

        // If all clarifications were auto-applied, re-process
        if (remainingClarifications.Count == 0 && autoAppliedAnswers.Count > 0)
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Preferences.FormatAutoAppliedPreferences(autoAppliedAnswers), null, ct);

            var fullContext = ConversationContextBuilder.Build(conversation.MessageHistory);
            var retryResult = await _mealProcessor.ProcessMealDescriptionAsync(fullContext, context.ChatId, ct);

            if (!retryResult.NeedsClarification && retryResult.MealRequest != null)
            {
                await _validationOrchestrator.AttemptMealLoggingAsync(context.ChatId, context.UserInfo, retryResult.MealRequest, ct);
                return;
            }
            remainingClarifications = retryResult.Clarifications;
        }

        if (remainingClarifications.Count > 0)
        {
            conversation.State = ConversationState.AwaitingClarification;
            conversation.PendingClarifications = remainingClarifications;
            conversation.PendingMealRequest = result.MealRequest;

            var clarificationMessage = ClarificationFormatter.Format(remainingClarifications);
            conversation.MessageHistory.Add(new ConversationMessage
            {
                Role = "assistant",
                Content = clarificationMessage,
                Timestamp = DateTime.UtcNow
            });

            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Meal.NeedsClarificationPrefix + clarificationMessage, "HTML", ct);
        }
    }

    private static string ReplaceAliasesInText(string text, List<DetectedAlias> detectedAliases)
    {
        if (detectedAliases.Count == 0) return text;

        var sortedAliases = detectedAliases.OrderByDescending(a => a.StartIndex).ToList();
        var result = text;

        foreach (var detected in sortedAliases)
        {
            var before = result.Substring(0, detected.StartIndex);
            var after = result.Substring(detected.StartIndex + detected.Length);
            result = before + detected.Alias.ResolvedFoodName + after;
        }

        return result;
    }

    /// <summary>
    /// Formats all user preferences into a string for the LLM prompt.
    /// </summary>
    private static string FormatPreferencesForPrompt(
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
