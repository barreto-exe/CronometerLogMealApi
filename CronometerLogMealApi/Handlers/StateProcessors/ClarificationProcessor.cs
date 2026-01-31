using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Models;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Processes clarification responses from the user.
/// </summary>
public class ClarificationProcessor : IStateProcessor
{
    private readonly ITelegramService _telegramService;
    private readonly IMealProcessor _mealProcessor;
    private readonly IUserMemoryService? _memoryService;
    private readonly IMealValidationOrchestrator _validationOrchestrator;
    private readonly ILogger<ClarificationProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingClarification;

    public ClarificationProcessor(
        ITelegramService telegramService,
        IMealProcessor mealProcessor,
        IMealValidationOrchestrator validationOrchestrator,
        ILogger<ClarificationProcessor> logger,
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
        
        // Add clarification to history
        conversation.MessageHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = context.Text,
            Timestamp = DateTime.UtcNow
        });

        // Record clarification patterns for learning
        if (_memoryService != null && conversation.PendingClarifications.Count > 0)
        {
            await RecordClarificationPatternsAsync(context, ct);
        }

        conversation.State = ConversationState.Processing;
        await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Meal.ProcessingResponse, null, ct);

        try
        {
            var fullContext = ConversationContextBuilder.Build(conversation.MessageHistory);
            var result = await _mealProcessor.ProcessMealDescriptionAsync(fullContext, ct);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                conversation.State = ConversationState.AwaitingClarification;
                await _telegramService.SendMessageAsync(context.ChatId,
                    TelegramMessages.Meal.FormatClarificationResponseError(result.ErrorMessage), null, ct);
                return;
            }

            if (result.NeedsClarification && result.Clarifications.Count > 0)
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
                    TelegramMessages.Meal.StillNeedsClarification + clarificationMessage, "HTML", ct);
                return;
            }

            // All clarified, try to log
            await _validationOrchestrator.AttemptMealLoggingAsync(context.ChatId, context.UserInfo, result.MealRequest!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing clarification for chatId {ChatId}", context.ChatId);
            conversation.State = ConversationState.AwaitingClarification;
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Meal.ClarificationError, null, ct);
        }
    }

    private async Task RecordClarificationPatternsAsync(StateContext context, CancellationToken ct)
    {
        var conversation = context.Conversation;
        var parsedResponses = ClarificationResponseParser.Parse(context.Text, conversation.PendingClarifications);

        foreach (var (clarification, answer) in parsedResponses)
        {
            var termToRecord = !string.IsNullOrEmpty(clarification.OriginalTerm)
                ? clarification.OriginalTerm
                : clarification.ItemName;

            var wasConfirmed = await _memoryService!.RecordClarificationPatternAsync(
                context.ChatId,
                termToRecord,
                clarification.Type.ToString(),
                answer,
                ct);

            if (wasConfirmed)
            {
                _logger.LogInformation("New clarification preference confirmed: '{Term}' + {Type} -> '{Answer}'",
                    termToRecord, clarification.Type, answer);
            }
        }
    }
}
