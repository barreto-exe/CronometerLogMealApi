using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Helpers;
using CronometerLogMealApi.Models;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Processes user confirmation responses for meal logging.
/// </summary>
public class ConfirmationProcessor : IStateProcessor
{
    private readonly ITelegramService _telegramService;
    private readonly IMealProcessor _mealProcessor;
    private readonly IUserMemoryService? _memoryService;
    private readonly IMealValidationOrchestrator _validationOrchestrator;
    private readonly IAlternativeSearchHandler _alternativeSearchHandler;
    private readonly ILogger<ConfirmationProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingConfirmation;

    public ConfirmationProcessor(
        ITelegramService telegramService,
        IMealProcessor mealProcessor,
        IMealValidationOrchestrator validationOrchestrator,
        IAlternativeSearchHandler alternativeSearchHandler,
        ILogger<ConfirmationProcessor> logger,
        IUserMemoryService? memoryService = null)
    {
        _telegramService = telegramService;
        _mealProcessor = mealProcessor;
        _validationOrchestrator = validationOrchestrator;
        _alternativeSearchHandler = alternativeSearchHandler;
        _logger = logger;
        _memoryService = memoryService;
    }

    public async Task ProcessAsync(StateContext context, CancellationToken ct)
    {
        var conversation = context.Conversation;
        var text = context.Text.Trim();

        // Check if user wants to search alternatives for a specific item
        if (int.TryParse(text, out int itemIndex) && 
            conversation.ValidatedFoods != null &&
            itemIndex >= 1 && itemIndex <= conversation.ValidatedFoods.Count)
        {
            await _alternativeSearchHandler.HandleAsync(context.ChatId, context.UserInfo, itemIndex - 1, ct);
            return;
        }

        // User sent text, treat as corrections/changes
        await _telegramService.SendMessageAsync(context.ChatId, 
            TelegramMessages.Meal.ProcessingChanges, null, ct);

        conversation.MessageHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = context.Text,
            Timestamp = DateTime.UtcNow
        });

        conversation.State = ConversationState.Processing;

        try
        {
            var fullContext = ConversationContextBuilder.Build(conversation.MessageHistory);
            // Load user preferences for the LLM prompt
            var userPreferences = await UserPreferencesHelper.LoadFormattedPreferencesAsync(_memoryService, context.ChatId, ct);
            var result = await _mealProcessor.ProcessMealDescriptionAsync(fullContext, context.ChatId, userPreferences, ct);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                conversation.State = ConversationState.AwaitingMealDescription;
                await _telegramService.SendMessageAsync(context.ChatId,
                    TelegramMessages.Meal.FormatDescriptionError(result.ErrorMessage), null, ct);
                return;
            }

            if (result.NeedsClarification)
            {
                var originalInput = ConversationContextBuilder.GetOriginalDescription(conversation.MessageHistory);

                conversation.State = ConversationState.AwaitingClarification;
                conversation.PendingClarifications = result.Clarifications;
                conversation.PendingMealRequest = result.MealRequest;

                var clarificationMessage = ClarificationFormatter.Format(result.Clarifications);
                conversation.MessageHistory.Add(new ConversationMessage
                {
                    Role = "assistant",
                    Content = clarificationMessage,
                    Timestamp = DateTime.UtcNow
                });

                await _telegramService.SendMessageAsync(context.ChatId,
                    TelegramMessages.Meal.NeedsClarificationPrefix + clarificationMessage, "HTML", ct);
                return;
            }

            await _validationOrchestrator.AttemptMealLoggingAsync(context.ChatId, context.UserInfo, result.MealRequest!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing change request for chatId {ChatId}", context.ChatId);
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Meal.ChangeError, null, ct);
            conversation.State = ConversationState.AwaitingConfirmation;
        }
    }
}

/// <summary>
/// Interface for handling alternative food search.
/// </summary>
public interface IAlternativeSearchHandler
{
    Task HandleAsync(string chatId, CronometerUserInfo userInfo, int itemIndex, CancellationToken ct);
}
