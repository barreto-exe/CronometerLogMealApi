namespace CronometerLogMealApi.Services;

public class TelegramPollingHostedService : BackgroundService
{
    private readonly ILogger<TelegramPollingHostedService> _logger;
    private readonly TelegramService _service;

    private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(500);

    public TelegramPollingHostedService(ILogger<TelegramPollingHostedService> logger, TelegramService service)
    {
        _logger = logger;
        _service = service;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure service is initialized
        await _service.InitAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var res = await _service.GetTelegramUpdates(null, stoppingToken);
                if (res?.Ok == true && res.Result is { Count: > 0 })
                {
                    foreach (var update in res.Result)
                    {
                        var msg = update.Message ?? update.EditedMessage;
                        var text = msg?.Text;
                        var chatId = msg?.Chat?.Id;
                        if (!string.IsNullOrWhiteSpace(text) && chatId.HasValue)
                        {
                            _logger.LogInformation("[Telegram] {ChatId}: {Text}", chatId.Value, text);
                            // Example: simple echo reply
                            await _service.SendMessageAsync(chatId.Value.ToString(), $"Echo: {text}", null, stoppingToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while polling Telegram updates");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }
    }
}
