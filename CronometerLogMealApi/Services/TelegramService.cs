using System.Threading;
using System.Threading.Tasks;
using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Clients.TelegramClient;
using CronometerLogMealApi.Clients.TelegramClient.Models;
using CronometerLogMealApi.Clients.TelegramClient.Requests;

namespace CronometerLogMealApi.Services;

/// <summary>
/// Service for Telegram messaging operations.
/// Implements ITelegramService for dependency injection.
/// </summary>
public class TelegramService : ITelegramService
{
    private readonly TelegramHttpClient _telegram;
    private readonly ILogger<TelegramService> _logger;

    private long _lastUpdateId = 0;
    private int _initialized = 0; // 0 = false, 1 = true

    public TelegramService(TelegramHttpClient telegram, ILogger<TelegramService> logger)
    {
        _telegram = telegram;
        _logger = logger;
        // Kick off initialization without blocking
        _ = InitAsync(CancellationToken.None);
    }

    public long LastUpdateId => Interlocked.Read(ref _lastUpdateId);

    // Runs only once; safe under concurrency
    public async Task InitAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return; // already initialized
        }

        try
        {
            // Get just one latest update to initialize the lastUpdateId
            var updates = await _telegram.GetUpdatesAsync(new GetUpdatesRequest
            {
                Limit = 1,
                Timeout = 0
            }, ct);

            if (updates?.Ok == true && updates.Result is { Count: > 0 })
            {
                var max = updates.Result.Max(u => u.UpdateId);
                Interlocked.Exchange(ref _lastUpdateId, max + 1);
            }
            else
            {
                Interlocked.Exchange(ref _lastUpdateId, 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TelegramService initialization failed; defaulting LastUpdateId = 0");
            Interlocked.Exchange(ref _lastUpdateId, 0);
        }
    }

    public async Task<GetUpdatesResponse?> GetTelegramUpdates(long? offset, CancellationToken ct)
    {
        // Ensure initialized before first use
        await InitAsync(ct);

        var effectiveOffset = offset ?? LastUpdateId;

        var req = new GetUpdatesRequest
        {
            Offset = effectiveOffset,
            Limit = 100,
            Timeout = 0
        };

        var res = await _telegram.GetUpdatesAsync(req, ct);

        if (res?.Ok == true && res.Result != null && res.Result.Count > 0)
        {
            var last = res.Result.Max(u => u.UpdateId);
            Interlocked.Exchange(ref _lastUpdateId, last + 1);
        }

        return res;
    }

    public Task<SendMessageResponse?> SendMessageAsync(string chatId, string text, string? parseMode, CancellationToken ct)
    {
        var req = new SendMessageRequest
        {
            ChatId = chatId,
            Text = text,
            ParseMode = parseMode
        };
        return _telegram.SendMessageAsync(req, ct);
    }
}
