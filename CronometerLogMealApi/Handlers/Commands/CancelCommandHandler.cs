using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Models;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.Commands;

/// <summary>
/// Handles the /cancel command to abort the current session.
/// </summary>
public class CancelCommandHandler : ICommandHandler
{
    private readonly ITelegramService _telegramService;
    private readonly ILogger<CancelCommandHandler> _logger;

    public CancelCommandHandler(
        ITelegramService telegramService,
        ILogger<CancelCommandHandler> logger)
    {
        _telegramService = telegramService;
        _logger = logger;
    }

    public bool CanHandle(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var trimmed = command.Trim();
        return trimmed.StartsWith("/cancel", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("/cancelar", StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(CommandContext context, CancellationToken ct)
    {
        if (context.UserInfo == null)
        {
            await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Session.NoActiveSession, null, ct);
            return;
        }

        if (context.UserInfo.Conversation == null || context.UserInfo.Conversation.State == ConversationState.Idle)
        {
            await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Session.NoActiveSession, null, ct);
            return;
        }

        context.UserInfo.Conversation = null;

        await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Session.Cancelled, null, ct);
        
        _logger.LogInformation("Cancelled session for chatId {ChatId}", context.ChatId);
    }
}
