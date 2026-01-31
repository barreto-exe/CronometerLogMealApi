using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Models;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Processes preference action responses (create, delete, exit).
/// </summary>
public class PreferenceActionProcessor : IStateProcessor
{
    private readonly ITelegramService _telegramService;
    private readonly IUserMemoryService? _memoryService;
    private readonly ILogger<PreferenceActionProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingPreferenceAction;

    public PreferenceActionProcessor(
        ITelegramService telegramService,
        ILogger<PreferenceActionProcessor> logger,
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

        if (trimmed == "1" || trimmed.Contains("crear") || trimmed.Contains("nuevo"))
        {
            conversation.State = ConversationState.AwaitingAliasInput;
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Preferences.CreateAliasPrompt, "HTML", ct);
        }
        else if (trimmed == "2" || trimmed.Contains("eliminar") || trimmed.Contains("borrar"))
        {
            await HandleDeleteAliasFlow(context, ct);
        }
        else if (trimmed == "3" || trimmed.Contains("salir") || trimmed.Contains("cancelar"))
        {
            conversation.State = ConversationState.Idle;
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Preferences.ExitedPreferences, null, ct);
        }
        else
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Preferences.InvalidOption, null, ct);
        }
    }

    private async Task HandleDeleteAliasFlow(StateContext context, CancellationToken ct)
    {
        if (_memoryService == null)
        {
            await _telegramService.SendMessageAsync(context.ChatId, 
                TelegramMessages.Preferences.ServiceNotAvailable, null, ct);
            return;
        }

        var aliases = await _memoryService.GetUserAliasesAsync(context.ChatId, ct);
        
        if (aliases.Count == 0)
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Preferences.NoAliasesToDelete, null, ct);
            context.Conversation.State = ConversationState.Idle;
            return;
        }

        context.Conversation.State = ConversationState.AwaitingAliasDeleteConfirm;
        var aliasTuples = aliases.Take(15).Select(a => (a.InputTerm, a.ResolvedFoodName));
        var message = TelegramMessages.Preferences.FormatDeleteAliasMenu(aliasTuples);

        await _telegramService.SendMessageAsync(context.ChatId, message, "HTML", ct);
    }
}

/// <summary>
/// Processes alias input responses (the term the user wants to create an alias for).
/// </summary>
public class AliasInputProcessor : IStateProcessor
{
    private readonly ITelegramService _telegramService;
    private readonly ILogger<AliasInputProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingAliasInput;

    public AliasInputProcessor(
        ITelegramService telegramService,
        ILogger<AliasInputProcessor> logger)
    {
        _telegramService = telegramService;
        _logger = logger;
    }

    public async Task ProcessAsync(StateContext context, CancellationToken ct)
    {
        var conversation = context.Conversation;
        conversation.Touch();
        conversation.CurrentAliasInputTerm = context.Text.Trim();
        conversation.State = ConversationState.AwaitingFoodSearch;

        await _telegramService.SendMessageAsync(context.ChatId,
            TelegramMessages.Preferences.FormatTermSaved(context.Text.Trim()), "HTML", ct);
    }
}

/// <summary>
/// Processes food search responses during alias creation.
/// </summary>
public class FoodSearchProcessor : IStateProcessor
{
    private readonly ITelegramService _telegramService;
    private readonly ICronometerService _cronometerService;
    private readonly ILogger<FoodSearchProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingFoodSearch;

    public FoodSearchProcessor(
        ITelegramService telegramService,
        ICronometerService cronometerService,
        ILogger<FoodSearchProcessor> logger)
    {
        _telegramService = telegramService;
        _cronometerService = cronometerService;
        _logger = logger;
    }

    public async Task ProcessAsync(StateContext context, CancellationToken ct)
    {
        var conversation = context.Conversation;
        conversation.Touch();
        
        await _telegramService.SendMessageAsync(context.ChatId, 
            TelegramMessages.Preferences.SearchPrompt, null, ct);

        try
        {
            var auth = new AuthPayload
            {
                UserId = context.UserInfo.UserId!.Value,
                Token = context.UserInfo.SessionKey!
            };

            var (_, _, _, candidates) = await _cronometerService.SearchFoodWithCandidatesAsync(
                context.Text.Trim(), auth, ct);

            if (candidates.Count == 0)
            {
                await _telegramService.SendMessageAsync(context.ChatId,
                    TelegramMessages.Preferences.NoSearchResults, null, ct);
                return;
            }

            conversation.CurrentSearchResults = candidates;
            conversation.State = ConversationState.AwaitingFoodSelection;

            var results = candidates.Take(10).Select(c => (c.Food.Name, c.SourceTab));
            var message = TelegramMessages.Preferences.FormatSearchResults(results);

            await _telegramService.SendMessageAsync(context.ChatId, message, "HTML", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching food for alias");
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Preferences.SearchError, null, ct);
        }
    }
}

/// <summary>
/// Processes food selection responses during alias creation.
/// </summary>
public class FoodSelectionProcessor : IStateProcessor
{
    private readonly ITelegramService _telegramService;
    private readonly IUserMemoryService? _memoryService;
    private readonly FoodSearchProcessor _foodSearchProcessor;
    private readonly ILogger<FoodSelectionProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingFoodSelection;

    public FoodSelectionProcessor(
        ITelegramService telegramService,
        FoodSearchProcessor foodSearchProcessor,
        ILogger<FoodSelectionProcessor> logger,
        IUserMemoryService? memoryService = null)
    {
        _telegramService = telegramService;
        _foodSearchProcessor = foodSearchProcessor;
        _logger = logger;
        _memoryService = memoryService;
    }

    public async Task ProcessAsync(StateContext context, CancellationToken ct)
    {
        var conversation = context.Conversation;
        conversation.Touch();

        if (int.TryParse(context.Text.Trim(), out int selection) && 
            selection >= 1 && 
            selection <= conversation.CurrentSearchResults.Count)
        {
            var selected = conversation.CurrentSearchResults[selection - 1];
            var inputTerm = conversation.CurrentAliasInputTerm ?? context.Text;

            if (_memoryService != null)
            {
                await _memoryService.SaveAliasAsync(
                    context.ChatId,
                    inputTerm,
                    selected.Food.Name,
                    selected.Food.Id,
                    selected.SourceTab,
                    isManual: true,
                    ct);

                await _telegramService.SendMessageAsync(context.ChatId,
                    TelegramMessages.Preferences.FormatAliasSaved(inputTerm, selected.Food.Name),
                    "HTML", ct);
            }

            // Reset state
            conversation.CurrentAliasInputTerm = null;
            conversation.CurrentSearchResults.Clear();
            conversation.State = ConversationState.Idle;
        }
        else
        {
            // User typed a new search term
            await _foodSearchProcessor.ProcessAsync(context, ct);
        }
    }
}

/// <summary>
/// Processes alias delete confirmation responses.
/// </summary>
public class AliasDeleteConfirmProcessor : IStateProcessor
{
    private readonly ITelegramService _telegramService;
    private readonly IUserMemoryService? _memoryService;
    private readonly ILogger<AliasDeleteConfirmProcessor> _logger;

    public ConversationState HandledState => ConversationState.AwaitingAliasDeleteConfirm;

    public AliasDeleteConfirmProcessor(
        ITelegramService telegramService,
        ILogger<AliasDeleteConfirmProcessor> logger,
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

        if (_memoryService == null)
        {
            await _telegramService.SendMessageAsync(context.ChatId, 
                TelegramMessages.Preferences.ServiceNotAvailable, null, ct);
            return;
        }

        var aliases = await _memoryService.GetUserAliasesAsync(context.ChatId, ct);

        if (int.TryParse(context.Text.Trim(), out int selection) && 
            selection >= 1 && selection <= aliases.Count)
        {
            var aliasToDelete = aliases[selection - 1];
            await _memoryService.DeleteAliasAsync(aliasToDelete.Id, ct);

            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Preferences.FormatAliasDeleted(
                    aliasToDelete.InputTerm, aliasToDelete.ResolvedFoodName),
                null, ct);

            conversation.State = ConversationState.Idle;
        }
        else
        {
            await _telegramService.SendMessageAsync(context.ChatId,
                TelegramMessages.Preferences.InvalidNumber, null, ct);
        }
    }
}
