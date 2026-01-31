using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Handlers.StateProcessors;
using CronometerLogMealApi.Models;
using CronometerLogMealApi.Models.UserMemory;
using CronometerLogMealApi.Requests;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Services;

/// <summary>
/// Orchestrates meal validation and logging operations.
/// </summary>
public class MealValidationOrchestrator : IMealValidationOrchestrator, IAlternativeSearchHandler
{
    private readonly ITelegramService _telegramService;
    private readonly ICronometerService _cronometerService;
    private readonly CronometerHttpClient _cronometerClient;
    private readonly IUserMemoryService? _memoryService;
    private readonly ILogger<MealValidationOrchestrator> _logger;

    public MealValidationOrchestrator(
        ITelegramService telegramService,
        ICronometerService cronometerService,
        CronometerHttpClient cronometerClient,
        ILogger<MealValidationOrchestrator> logger,
        IUserMemoryService? memoryService = null)
    {
        _telegramService = telegramService;
        _cronometerService = cronometerService;
        _cronometerClient = cronometerClient;
        _logger = logger;
        _memoryService = memoryService;
    }

    public async Task AttemptMealLoggingAsync(
        string chatId, 
        CronometerUserInfo userInfo, 
        LogMealRequest request, 
        CancellationToken ct)
    {
        await _telegramService.SendMessageAsync(chatId, TelegramMessages.Meal.ValidatingWithCronometer, null, ct);

        var conversation = userInfo.Conversation!;
        var auth = new AuthPayload { UserId = userInfo.UserId!.Value, Token = userInfo.SessionKey! };

        var (validatedItems, notFoundItems) = await _cronometerService.ValidateMealItemsAsync(
            request.Items,
            auth,
            chatId,
            conversation.DetectedAliases,
            conversation.OriginalDescription,
            ct);

        if (notFoundItems.Count > 0)
        {
            conversation.State = ConversationState.AwaitingClarification;
            conversation.PendingClarifications = notFoundItems
                .Select(item => new ClarificationItem
                {
                    Type = ClarificationType.FoodNotFound,
                    ItemName = item,
                    Question = $"¿Podrías darme un nombre alternativo para \"{item}\"?"
                })
                .ToList();

            await _telegramService.SendMessageAsync(chatId,
                TelegramMessages.Meal.FormatNotFoundItems(notFoundItems), "HTML", ct);
            return;
        }

        // All items validated successfully
        conversation.PendingMealRequest = request;
        conversation.ValidatedFoods = validatedItems;
        conversation.State = ConversationState.AwaitingConfirmation;
        conversation.PendingLearnings.Clear();

        // Build summary message
        var itemsSummary = string.Join("\n", validatedItems.Select((item, idx) =>
        {
            var aliasIndicator = item.WasResolvedFromAlias ? " 🧠" : "";
            return $"{idx + 1}. {item.DisplayQuantity} de <b>{item.FoodName}</b>{aliasIndicator}";
        }));

        var hasMemoryItems = validatedItems.Any(v => v.WasResolvedFromAlias);
        var time = request.Date.ToString("h:mm tt");
        var category = request.Category.ToUpper();
        var message = TelegramMessages.Meal.FormatConfirmation(time, category, itemsSummary, hasMemoryItems);

        await _telegramService.SendMessageAsync(chatId, message, "HTML", ct);
    }

    public async Task HandleAsync(string chatId, CronometerUserInfo userInfo, int itemIndex, CancellationToken ct)
    {
        var conversation = userInfo.Conversation!;
        var item = conversation.ValidatedFoods[itemIndex];

        await _telegramService.SendMessageAsync(chatId,
            TelegramMessages.Search.FormatSearchingAlternatives(item.OriginalName), "HTML", ct);

        try
        {
            var auth = new AuthPayload { UserId = userInfo.UserId!.Value, Token = userInfo.SessionKey! };
            var (_, _, _, candidates) = await _cronometerService.SearchFoodWithCandidatesAsync(item.OriginalName, auth, ct);

            if (candidates.Count <= 1)
            {
                await _telegramService.SendMessageAsync(chatId,
                    TelegramMessages.Search.NoAlternatives, null, ct);
                return;
            }

            conversation.CurrentSearchResults = candidates;
            conversation.CurrentSearchItemIndex = itemIndex;
            conversation.State = ConversationState.AwaitingFoodSearchSelection;

            var alternatives = candidates.Take(10).Select(c => (c.Food.Name, c.SourceTab, c.Food.Id));
            var message = TelegramMessages.Search.FormatAlternatives(
                item.OriginalName, item.FoodName, item.FoodId, alternatives);

            await _telegramService.SendMessageAsync(chatId, message, "HTML", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching alternatives for chatId {ChatId}", chatId);
            await _telegramService.SendMessageAsync(chatId,
                TelegramMessages.Search.AlternativesError, null, ct);
        }
    }
}
