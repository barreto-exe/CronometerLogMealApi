using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Models;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Processes OCR correction responses from the user.
/// </summary>
public class OcrCorrectionProcessor : IStateProcessor
{
    private readonly ITelegramService _telegramService;
    private readonly IMealProcessor _mealProcessor;
    private readonly IMealValidationOrchestrator _validationOrchestrator;
    private readonly ILogger<OcrCorrectionProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingOCRCorrection;

    public OcrCorrectionProcessor(
        ITelegramService telegramService,
        IMealProcessor mealProcessor,
        IMealValidationOrchestrator validationOrchestrator,
        ILogger<OcrCorrectionProcessor> logger)
    {
        _telegramService = telegramService;
        _mealProcessor = mealProcessor;
        _validationOrchestrator = validationOrchestrator;
        _logger = logger;
    }

    public async Task ProcessAsync(StateContext context, CancellationToken ct)
    {
        var conversation = context.Conversation;

        if (string.IsNullOrWhiteSpace(conversation.OcrExtractedText))
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Ocr.NoOcrTextSaved, null, ct);
            return;
        }

        // Process OCR text with user corrections
        await ProcessOcrTextAsync(context.ChatId, context.UserInfo, 
            conversation.OcrExtractedText, context.Text, ct);
    }

    /// <summary>
    /// Processes OCR text with optional user corrections.
    /// </summary>
    public async Task ProcessOcrTextAsync(
        string chatId, 
        CronometerUserInfo userInfo, 
        string ocrText, 
        string? corrections, 
        CancellationToken ct)
    {
        var conversation = userInfo.Conversation!;
        conversation.State = ConversationState.Processing;
        await _telegramService.SendMessageAsync(chatId, TelegramMessages.Meal.Processing, null, ct);

        try
        {
            string fullDescription;
            if (!string.IsNullOrWhiteSpace(corrections))
            {
                fullDescription = $"{ocrText}\n\nCORRECCIONES DEL USUARIO: {corrections}";
                _logger.LogInformation("Processing OCR with corrections for chatId {ChatId}", chatId);
            }
            else
            {
                fullDescription = ocrText;
            }

            conversation.OriginalDescription = fullDescription;
            conversation.MessageHistory.Add(new ConversationMessage
            {
                Role = "user",
                Content = fullDescription,
                Timestamp = DateTime.UtcNow
            });

            var result = await _mealProcessor.ProcessMealDescriptionAsync(fullDescription, chatId, ct);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                conversation.State = ConversationState.AwaitingMealDescription;
                await _telegramService.SendMessageAsync(chatId,
                    TelegramMessages.Meal.FormatDescriptionError(result.ErrorMessage), null, ct);
                return;
            }

            if (result.NeedsClarification && result.Clarifications.Count > 0)
            {
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

                await _telegramService.SendMessageAsync(chatId,
                    TelegramMessages.Meal.NeedsClarificationPrefix + clarificationMessage, "HTML", ct);
                return;
            }

            await _validationOrchestrator.AttemptMealLoggingAsync(chatId, userInfo, result.MealRequest!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OCR text for chatId {ChatId}", chatId);
            conversation.State = ConversationState.AwaitingOCRCorrection;
            await _telegramService.SendMessageAsync(chatId,
                TelegramMessages.Ocr.OcrProcessingError, null, ct);
        }
    }
}
