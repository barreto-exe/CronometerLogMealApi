using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Models;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Processes food selection responses when the user is choosing an alternative food
/// from search results during meal confirmation.
/// </summary>
public class FoodSearchSelectionProcessor : IStateProcessor
{
    private readonly ITelegramService _telegramService;
    private readonly ILogger<FoodSearchSelectionProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingFoodSearchSelection;

    public FoodSearchSelectionProcessor(
        ITelegramService telegramService,
        ILogger<FoodSearchSelectionProcessor> logger)
    {
        _telegramService = telegramService;
        _logger = logger;
    }

    public async Task ProcessAsync(StateContext context, CancellationToken ct)
    {
        var conversation = context.Conversation;
        conversation.Touch();

        var text = context.Text.Trim();

        // Check if user typed a number to select an alternative
        if (int.TryParse(text, out int selection) &&
            selection >= 1 &&
            selection <= conversation.CurrentSearchResults.Count)
        {
            var selected = conversation.CurrentSearchResults[selection - 1];
            var itemIndex = conversation.CurrentSearchItemIndex;

            if (itemIndex.HasValue && itemIndex.Value < conversation.ValidatedFoods.Count)
            {
                var originalItem = conversation.ValidatedFoods[itemIndex.Value];

                // Update the validated food with the new selection
                originalItem.FoodId = selected.Food.Id;
                originalItem.FoodName = selected.Food.Name;
                originalItem.SourceTab = selected.SourceTab;

                _logger.LogInformation(
                    "User selected alternative for item {Index}: {OldName} -> {NewName}",
                    itemIndex.Value, originalItem.OriginalName, selected.Food.Name);

                // Clear search state
                conversation.CurrentSearchResults.Clear();
                conversation.CurrentSearchItemIndex = null;

                // Return to confirmation state
                conversation.State = ConversationState.AwaitingConfirmation;

                // Rebuild and send confirmation message
                await SendUpdatedConfirmationAsync(context, ct);
            }
            else
            {
                _logger.LogWarning(
                    "Invalid item index {Index} for chatId {ChatId}",
                    itemIndex, context.ChatId);

                await _telegramService.SendMessageAsync(context.ChatId,
                    TelegramMessages.Search.AlternativesError, null, ct);

                conversation.State = ConversationState.AwaitingConfirmation;
            }
        }
        else
        {
            // Invalid input - tell user to select a valid number
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Search.InvalidSelection, null, ct);
        }
    }

    private async Task SendUpdatedConfirmationAsync(StateContext context, CancellationToken ct)
    {
        var conversation = context.Conversation;
        var request = conversation.PendingMealRequest;

        if (request == null || conversation.ValidatedFoods.Count == 0)
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Session.NoValidatedData, null, ct);
            return;
        }

        var itemsSummary = string.Join("\n", conversation.ValidatedFoods.Select((item, idx) =>
        {
            var aliasIndicator = item.WasResolvedFromAlias ? " 🧠" : "";
            return $"{idx + 1}. {item.DisplayQuantity} de <b>{item.FoodName}</b>{aliasIndicator}";
        }));

        var hasMemoryItems = conversation.ValidatedFoods.Any(v => v.WasResolvedFromAlias);
        var time = request.Date.ToString("h:mm tt");
        var category = request.Category.ToUpper();
        var message = TelegramMessages.Meal.FormatConfirmation(time, category, itemsSummary, hasMemoryItems);

        await _telegramService.SendMessageAsync(context.ChatId, message, "HTML", ct);
    }
}
