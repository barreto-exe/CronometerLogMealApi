using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Models;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.Commands;

/// <summary>
/// Handles the /preferences command for managing user preferences and aliases.
/// </summary>
public class PreferencesCommandHandler : ICommandHandler
{
    private readonly ITelegramService _telegramService;
    private readonly IUserMemoryService? _memoryService;
    private readonly ILogger<PreferencesCommandHandler> _logger;

    public PreferencesCommandHandler(
        ITelegramService telegramService,
        ILogger<PreferencesCommandHandler> logger,
        IUserMemoryService? memoryService = null)
    {
        _telegramService = telegramService;
        _logger = logger;
        _memoryService = memoryService;
    }

    public bool CanHandle(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var trimmed = command.Trim();
        return trimmed.StartsWith("/preferences", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("/preferencias", StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(CommandContext context, CancellationToken ct)
    {
        if (_memoryService == null)
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Preferences.ServiceNotAvailable, null, ct);
            return;
        }

        if (!context.IsAuthenticated)
        {
            await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Auth.LoginRequired, "HTML", ct);
            return;
        }

        // Initialize conversation for preferences if needed
        if (context.UserInfo!.Conversation == null || context.UserInfo.Conversation.IsExpired)
        {
            context.UserInfo.Conversation = new ConversationSession
            {
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };
        }

        context.UserInfo.Conversation.State = ConversationState.AwaitingPreferenceAction;
        context.UserInfo.Conversation.Touch();

        var aliases = await _memoryService.GetUserAliasesAsync(context.ChatId, ct);

        var aliasTuples = aliases.Select(a => (a.InputTerm, a.ResolvedFoodName, a.UseCount));
        var message = TelegramMessages.Preferences.FormatPreferencesMenu(aliasTuples);

        await _telegramService.SendMessageAsync(context.ChatId, message, "HTML", ct);
        
        _logger.LogInformation("Opened preferences menu for chatId {ChatId}", context.ChatId);
    }
}
