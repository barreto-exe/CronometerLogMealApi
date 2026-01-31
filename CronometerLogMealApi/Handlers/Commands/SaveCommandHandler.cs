using System.Text.Json;
using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Helpers;
using CronometerLogMealApi.Models;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.Commands;

/// <summary>
/// Handles the /save command to persist meal data to Cronometer.
/// </summary>
public class SaveCommandHandler : ICommandHandler
{
    private readonly ITelegramService _telegramService;
    private readonly CronometerHttpClient _cronometerClient;
    private readonly IUserMemoryService? _memoryService;
    private readonly ILogger<SaveCommandHandler> _logger;

    public SaveCommandHandler(
        ITelegramService telegramService,
        CronometerHttpClient cronometerClient,
        ILogger<SaveCommandHandler> logger,
        IUserMemoryService? memoryService = null)
    {
        _telegramService = telegramService;
        _cronometerClient = cronometerClient;
        _logger = logger;
        _memoryService = memoryService;
    }

    public bool CanHandle(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var trimmed = command.Trim();
        return trimmed.StartsWith("/save", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("/guardar", StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(CommandContext context, CancellationToken ct)
    {
        if (context.UserInfo?.Conversation == null)
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Session.NoSessionToSave, null, ct);
            return;
        }

        var conversation = context.UserInfo.Conversation;

        if (conversation.State != ConversationState.AwaitingConfirmation)
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Session.NoPendingChanges, null, ct);
            return;
        }

        if (conversation.PendingMealRequest == null || !conversation.ValidatedFoods.Any())
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Session.NoValidatedData, null, ct);
            conversation.State = ConversationState.Idle;
            return;
        }

        await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Meal.Saving, null, ct);

        try
        {
            var request = conversation.PendingMealRequest;
            var order = MealCategoryHelper.GetOrderForCategory(request.Category);

            var servingPayload = new AddMultiServingRequest
            {
                Servings = new List<ServingPayload>(),
                Auth = new AuthPayload
                {
                    UserId = context.UserInfo.UserId!.Value,
                    Token = context.UserInfo.SessionKey!
                }
            };

            foreach (var item in conversation.ValidatedFoods)
            {
                double grams = item.IsRawGrams 
                    ? item.Quantity 
                    : item.Quantity * item.MeasureGrams;

                servingPayload.Servings.Add(new ServingPayload
                {
                    Order = order,
                    Day = request.Date.ToString("yyyy-MM-dd"),
                    Time = request.LogTime == true ? request.Date.ToString("HH:m:s") : string.Empty,
                    UserId = context.UserInfo.UserId!.Value,
                    Type = "Serving",
                    FoodId = item.FoodId,
                    MeasureId = item.MeasureId,
                    Grams = grams
                });
            }

            var result = await _cronometerClient.AddMultiServingAsync(servingPayload, ct);

            bool hasFailed = result != null &&
                result.Raw.ValueKind == JsonValueKind.Object &&
                result.Raw.TryGetProperty("result", out var resultProp) &&
                string.Equals(resultProp.GetString(), "fail", StringComparison.OrdinalIgnoreCase);

            if (hasFailed)
            {
                await _telegramService.SendMessageAsync(context.ChatId, 
                    TelegramMessages.Meal.SaveError, null, ct);
                return;
            }

            var pendingLearnings = conversation.PendingLearnings;

            // Ask about saving learnings if applicable
            if (_memoryService != null && pendingLearnings.Count > 0)
            {
                conversation.State = ConversationState.AwaitingMemoryConfirmation;
                var learnings = pendingLearnings.Select(l => (l.OriginalTerm, l.ResolvedFoodName));
                var message = TelegramMessages.Preferences.FormatMemoryConfirmation(learnings);
                await _telegramService.SendMessageAsync(context.ChatId, message, "HTML", ct);
            }
            else
            {
                context.UserInfo.Conversation = null;
                await _telegramService.SendMessageAsync(context.ChatId,
                    TelegramMessages.Meal.SaveSuccess, "HTML", ct);
            }

            _logger.LogInformation("Successfully saved meal for chatId {ChatId}", context.ChatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving meal for chatId {ChatId}", context.ChatId);
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Meal.SaveRetryError, null, ct);
        }
    }
}
