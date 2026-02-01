using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Helpers;
using CronometerLogMealApi.Models;
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
    private readonly ISessionLogService? _sessionLogService;
    private readonly IMealValidationOrchestrator _validationOrchestrator;
    private readonly ILogger<MealDescriptionProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingMealDescription;

    public MealDescriptionProcessor(
        ITelegramService telegramService,
        IMealProcessor mealProcessor,
        IMealValidationOrchestrator validationOrchestrator,
        ILogger<MealDescriptionProcessor> logger,
        IUserMemoryService? memoryService = null,
        ISessionLogService? sessionLogService = null)
    {
        _telegramService = telegramService;
        _mealProcessor = mealProcessor;
        _validationOrchestrator = validationOrchestrator;
        _logger = logger;
        _memoryService = memoryService;
        _sessionLogService = sessionLogService;
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
            var userPreferences = await UserPreferencesHelper.LoadFormattedPreferencesAsync(_memoryService, context.ChatId, ct);
            if (userPreferences != null)
            {
                _logger.LogInformation("Formatted user preferences for LLM: {Preferences}", userPreferences);
            }

            // Process with LLM - user preferences (including aliases) are in the prompt
            // CronometerService.GetFoodWithMemoryAsync will also catch aliases during validation as a fallback
            var result = await _mealProcessor.ProcessMealDescriptionAsync(text, context.ChatId, userPreferences, ct);

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
            _sessionLogService?.LogError(context.ChatId, ex.Message, ex.GetType().Name, ex.StackTrace);
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
            var retryUserPreferences = await UserPreferencesHelper.LoadFormattedPreferencesAsync(_memoryService, context.ChatId, ct);
            var retryResult = await _mealProcessor.ProcessMealDescriptionAsync(fullContext, context.ChatId, retryUserPreferences, ct);

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
}
