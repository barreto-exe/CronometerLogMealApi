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
    private readonly ISessionLogService? _sessionLogService;

    public CancelCommandHandler(
        ITelegramService telegramService,
        ILogger<CancelCommandHandler> logger,
        ISessionLogService? sessionLogService = null)
    {
        _telegramService = telegramService;
        _logger = logger;
        _sessionLogService = sessionLogService;
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

        var originalDescription = context.UserInfo.Conversation?.OriginalDescription;
        context.UserInfo.Conversation = null;

        await _sessionLogService?.EndSessionAsync(context.ChatId, "cancelled", 0, originalDescription, ct)!;
        await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Session.Cancelled, null, ct);
        
        _logger.LogInformation("Cancelled session for chatId {ChatId}", context.ChatId);
    }
}
