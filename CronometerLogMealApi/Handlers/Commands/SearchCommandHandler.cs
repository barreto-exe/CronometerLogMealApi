using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Constants;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.Commands;

/// <summary>
/// Handles the /search command for manual food search.
/// </summary>
public class SearchCommandHandler : ICommandHandler
{
    private readonly ITelegramService _telegramService;
    private readonly ICronometerService _cronometerService;
    private readonly ILogger<SearchCommandHandler> _logger;

    public SearchCommandHandler(
        ITelegramService telegramService,
        ICronometerService cronometerService,
        ILogger<SearchCommandHandler> logger)
    {
        _telegramService = telegramService;
        _cronometerService = cronometerService;
        _logger = logger;
    }

    public bool CanHandle(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var trimmed = command.Trim();
        return trimmed.StartsWith("/search", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("/buscar", StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(CommandContext context, CancellationToken ct)
    {
        if (!context.IsAuthenticated)
        {
            await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Auth.LoginRequired, "HTML", ct);
            return;
        }

        // Extract search query from command
        var query = context.Text
            .Replace("/search", "", StringComparison.OrdinalIgnoreCase)
            .Replace("/buscar", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Search.Usage, null, ct);
            return;
        }

        await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Search.Searching, null, ct);

        try
        {
            var auth = new AuthPayload
            {
                UserId = context.UserInfo!.UserId!.Value,
                Token = context.UserInfo.SessionKey!
            };

            var (_, _, _, candidates) = await _cronometerService.SearchFoodWithCandidatesAsync(query, auth, ct);

            if (candidates.Count == 0)
            {
                await _telegramService.SendMessageAsync(context.ChatId,
                    TelegramMessages.Search.FormatNoResults(query), null, ct);
                return;
            }

            var results = candidates.Take(10).Select(c => (c.Food.Name, c.SourceTab, c.Score));
            var message = TelegramMessages.Search.FormatResults(query, results);

            await _telegramService.SendMessageAsync(context.ChatId, message, "HTML", ct);
            
            _logger.LogInformation("Search for '{Query}' returned {Count} results for chatId {ChatId}",
                query, candidates.Count, context.ChatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in search command for chatId {ChatId}", context.ChatId);
            await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Search.Error, null, ct);
        }
    }
}
