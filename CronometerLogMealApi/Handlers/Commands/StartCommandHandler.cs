using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Models;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.Commands;

/// <summary>
/// Handles the /start command to begin a new meal logging session.
/// </summary>
public class StartCommandHandler : ICommandHandler
{
    private readonly ITelegramService _telegramService;
    private readonly ILogger<StartCommandHandler> _logger;
    private readonly ISessionLogService? _sessionLogService;

    public StartCommandHandler(
        ITelegramService telegramService,
        ILogger<StartCommandHandler> logger,
        ISessionLogService? sessionLogService = null)
    {
        _telegramService = telegramService;
        _logger = logger;
        _sessionLogService = sessionLogService;
    }

    public bool CanHandle(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        return command.Trim().StartsWith("/start", StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(CommandContext context, CancellationToken ct)
    {
        if (!context.IsAuthenticated)
        {
            await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Auth.LoginRequired, "HTML", ct);
            return;
        }

        // Check if there's already an active session
        if (context.UserInfo!.Conversation != null && 
            !context.UserInfo.Conversation.IsExpired &&
            context.UserInfo.Conversation.State != ConversationState.Idle)
        {
            await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Session.AlreadyActive, null, ct);
            return;
        }

        // Initialize new conversation session
        _sessionLogService?.StartSession(context.ChatId);
        
        context.UserInfo.Conversation = new ConversationSession
        {
            State = ConversationState.AwaitingMealDescription,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            MessageHistory = new List<ConversationMessage>()
        };

        await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Meal.NewSessionStarted, "HTML", ct);
        _sessionLogService?.LogBotResponse(context.ChatId, TelegramMessages.Meal.NewSessionStarted);
        
        _logger.LogInformation("Started new meal session for chatId {ChatId}", context.ChatId);
    }
}
