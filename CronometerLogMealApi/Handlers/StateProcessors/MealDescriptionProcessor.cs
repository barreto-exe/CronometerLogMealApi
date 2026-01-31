using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Models;
using CronometerLogMealApi.Models.UserMemory;
using Microsoft.Extensions.Logging;

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
            // Detect and replace aliases before sending to LLM
            var textForLlm = text;
            if (_memoryService != null)
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

            // Process with LLM
            var result = await _mealProcessor.ProcessMealDescriptionAsync(textForLlm, ct);

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
            var retryResult = await _mealProcessor.ProcessMealDescriptionAsync(fullContext, ct);

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
}
