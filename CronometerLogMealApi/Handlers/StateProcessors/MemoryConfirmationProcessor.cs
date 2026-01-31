using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Models;
using CronometerLogMealApi.Models.UserMemory;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Processes memory confirmation responses (whether to save learned preferences).
/// </summary>
public class MemoryConfirmationProcessor : IStateProcessor
{
    private readonly ITelegramService _telegramService;
    private readonly IUserMemoryService? _memoryService;
    private readonly ILogger<MemoryConfirmationProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingMemoryConfirmation;

    public MemoryConfirmationProcessor(
        ITelegramService telegramService,
        ILogger<MemoryConfirmationProcessor> logger,
        IUserMemoryService? memoryService = null)
    {
        _telegramService = telegramService;
        _logger = logger;
        _memoryService = memoryService;
    }

    public async Task ProcessAsync(StateContext context, CancellationToken ct)
    {
        var conversation = context.Conversation;
        conversation.Touch();
        
        var trimmed = context.Text.Trim().ToLowerInvariant();
        var pendingLearnings = conversation.PendingLearnings;

        if (_memoryService == null || pendingLearnings.Count == 0)
        {
            context.UserInfo.Conversation = null;
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Preferences.Done, null, ct);
            return;
        }

        List<PendingLearning> learningsToSave;

        if (trimmed == "si" || trimmed == "sí" || trimmed == "yes" || trimmed == "s")
        {
            learningsToSave = pendingLearnings;
        }
        else if (trimmed == "no" || trimmed == "n")
        {
            context.UserInfo.Conversation = null;
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Preferences.NoPreferencesSaved, null, ct);
            return;
        }
        else
        {
            // Try to parse specific numbers
            var numbers = System.Text.RegularExpressions.Regex.Matches(trimmed, @"\d+")
                .Select(m => int.TryParse(m.Value, out int n) ? n : 0)
                .Where(n => n >= 1 && n <= pendingLearnings.Count)
                .ToList();

            if (numbers.Count > 0)
            {
                learningsToSave = numbers.Select(n => pendingLearnings[n - 1]).ToList();
            }
            else
            {
                await _telegramService.SendMessageAsync(context.ChatId,
                    TelegramMessages.Preferences.InvalidMemoryResponse, null, ct);
                return;
            }
        }

        // Save the selected learnings
        foreach (var learning in learningsToSave)
        {
            try
            {
                await _memoryService.SaveAliasAsync(
                    context.ChatId,
                    learning.OriginalTerm,
                    learning.ResolvedFoodName,
                    learning.ResolvedFoodId,
                    learning.SourceTab,
                    isManual: false,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save alias for {Term}", learning.OriginalTerm);
            }
        }

        context.UserInfo.Conversation = null;
        await _telegramService.SendMessageAsync(context.ChatId,
            TelegramMessages.Preferences.FormatPreferencesSaved(learningsToSave.Count), "HTML", ct);
    }
}
