using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Models;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.Commands;

/// <summary>
/// Handles the /continue command for OCR workflow.
/// </summary>
public class ContinueCommandHandler : ICommandHandler
{
    private readonly ITelegramService _telegramService;
    private readonly ILogger<ContinueCommandHandler> _logger;

    // Event to signal OCR processing should continue
    public event Func<string, Models.CronometerUserInfo, string, string?, CancellationToken, Task>? OnOcrContinue;

    public ContinueCommandHandler(
        ITelegramService telegramService,
        ILogger<ContinueCommandHandler> logger)
    {
        _telegramService = telegramService;
        _logger = logger;
    }

    public bool CanHandle(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var trimmed = command.Trim();
        return trimmed.StartsWith("/continue", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("/continuar", StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(CommandContext context, CancellationToken ct)
    {
        if (context.UserInfo?.Conversation == null)
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                "No hay una sesión activa. Usa /start para iniciar.", null, ct);
            return;
        }

        if (context.UserInfo.Conversation.State != ConversationState.AwaitingOCRCorrection)
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Ocr.ContinueOnlyAfterPhoto, null, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(context.UserInfo.Conversation.OcrExtractedText))
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Ocr.NoOcrTextSaved, null, ct);
            context.UserInfo.Conversation.State = ConversationState.Idle;
            return;
        }

        // Trigger OCR processing continuation
        if (OnOcrContinue != null)
        {
            await OnOcrContinue(context.ChatId, context.UserInfo, 
                context.UserInfo.Conversation.OcrExtractedText, null, ct);
        }
        
        _logger.LogInformation("Continuing OCR processing for chatId {ChatId}", context.ChatId);
    }
}
