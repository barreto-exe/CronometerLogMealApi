using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Clients.TelegramClient;
using CronometerLogMealApi.Clients.TelegramClient.Models;
using CronometerLogMealApi.Clients.TelegramClient.Requests;
using CronometerLogMealApi.Constants;
using CronometerLogMealApi.Handlers;
using CronometerLogMealApi.Handlers.StateProcessors;
using CronometerLogMealApi.Models;

namespace CronometerLogMealApi.Services;

/// <summary>
/// Hosted service that polls Telegram for updates and routes messages to appropriate handlers.
/// This is the refactored, cleaner version with separated concerns.
/// </summary>
public class TelegramPollingService : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(500);
    
    private readonly ILogger<TelegramPollingService> _logger;
    private readonly ITelegramService _telegramService;
    private readonly TelegramHttpClient _telegramClient;
    private readonly ISessionManager _sessionManager;
    private readonly IOcrService _ocrService;
    private readonly IUserMemoryService? _memoryService;
    
    // Command handlers
    private readonly IEnumerable<ICommandHandler> _commandHandlers;
    
    // State processors
    private readonly IDictionary<ConversationState, IStateProcessor> _stateProcessors;
    
    // OCR processing
    private readonly OcrCorrectionProcessor _ocrProcessor;

    public TelegramPollingService(
        ILogger<TelegramPollingService> logger,
        ITelegramService telegramService,
        TelegramHttpClient telegramClient,
        ISessionManager sessionManager,
        IOcrService ocrService,
        IEnumerable<ICommandHandler> commandHandlers,
        IEnumerable<IStateProcessor> stateProcessors,
        OcrCorrectionProcessor ocrProcessor,
        IUserMemoryService? memoryService = null)
    {
        _logger = logger;
        _telegramService = telegramService;
        _telegramClient = telegramClient;
        _sessionManager = sessionManager;
        _ocrService = ocrService;
        _commandHandlers = commandHandlers;
        _ocrProcessor = ocrProcessor;
        _memoryService = memoryService;
        
        // Build state processor dictionary for fast lookup
        _stateProcessors = stateProcessors.ToDictionary(p => p.HandledState);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _telegramService.InitAsync(stoppingToken);
        await RestoreSessionsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var res = await _telegramService.GetTelegramUpdates(null, stoppingToken);

                if (res?.Ok == true && res.Result is { Count: > 0 })
                {
                    foreach (var update in res.Result)
                    {
                        await HandleUpdateAsync(update, stoppingToken);
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
                // Shutdown requested
            }
        }
    }

    private async Task RestoreSessionsAsync(CancellationToken ct)
    {
        if (_memoryService == null)
        {
            _logger.LogInformation("Memory service not available, skipping session restoration");
            return;
        }

        try
        {
            var sessions = await _memoryService.GetAllActiveSessionsAsync(ct);

            foreach (var session in sessions)
            {
                var userInfo = new CronometerUserInfo
                {
                    Email = session.Email,
                    UserId = session.CronometerUserId,
                    SessionKey = session.SessionKey
                };

                _sessionManager.SetSession(session.TelegramChatId, userInfo);
                _logger.LogInformation("Restored session for {Email}", session.Email);
            }

            _logger.LogInformation("Restored {Count} user sessions", sessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring sessions");
        }
    }

    private async Task HandleUpdateAsync(TelegramUpdate? update, CancellationToken ct)
    {
        if (update == null) return;

        var msg = update.Message ?? update.EditedMessage;
        var text = msg?.Text;
        var chatId = msg?.Chat?.Id;
        var photo = msg?.Photo;
        var caption = msg?.Caption;

        if (!chatId.HasValue) return;

        var chatIdStr = chatId.Value.ToString();

        // Handle photo messages (OCR)
        if (photo != null && photo.Count > 0)
        {
            await HandlePhotoAsync(chatIdStr, photo, caption, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(text)) return;

        _logger.LogInformation("[Telegram] {ChatId}: {Text}", chatIdStr, text);

        // Get or create user context
        var userInfo = _sessionManager.GetSession(chatIdStr);

        // Check session expiry
        if (userInfo?.Conversation != null && userInfo.Conversation.IsExpired)
        {
            _logger.LogInformation("Session expired for chatId {ChatId}", chatIdStr);
            userInfo.Conversation = null;
            await _telegramService.SendMessageAsync(chatIdStr, TelegramMessages.Session.Expired, null, ct);
        }

        // Create command context
        var context = new CommandContext
        {
            ChatId = chatIdStr,
            Text = text,
            UserInfo = userInfo
        };

        // Try to handle as command first
        foreach (var handler in _commandHandlers)
        {
            if (handler.CanHandle(text))
            {
                await handler.HandleAsync(context, ct);
                return;
            }
        }

        // Route based on conversation state
        await RouteByConversationStateAsync(chatIdStr, userInfo, text, ct);
    }

    private async Task RouteByConversationStateAsync(
        string chatId, 
        CronometerUserInfo? userInfo, 
        string text, 
        CancellationToken ct)
    {
        if (userInfo == null)
        {
            await _telegramService.SendMessageAsync(chatId, TelegramMessages.Auth.NotAuthenticated, "HTML", ct);
            return;
        }

        if (userInfo.Conversation == null || userInfo.Conversation.State == ConversationState.Idle)
        {
            await _telegramService.SendMessageAsync(chatId, TelegramMessages.Session.UseStartToBegin, null, ct);
            return;
        }

        userInfo.Conversation.Touch();

        var state = userInfo.Conversation.State;

        if (state == ConversationState.Processing)
        {
            await _telegramService.SendMessageAsync(chatId, TelegramMessages.Meal.StillProcessing, null, ct);
            return;
        }

        if (_stateProcessors.TryGetValue(state, out var processor))
        {
            var context = new StateContext
            {
                ChatId = chatId,
                Text = text,
                UserInfo = userInfo
            };

            await processor.ProcessAsync(context, ct);
        }
        else
        {
            await _telegramService.SendMessageAsync(chatId, TelegramMessages.Session.UseStartForNew, null, ct);
        }
    }

    private async Task HandlePhotoAsync(
        string chatId, 
        List<TelegramPhotoSize> photos, 
        string? caption, 
        CancellationToken ct)
    {
        var userInfo = _sessionManager.GetSession(chatId);

        if (userInfo == null || string.IsNullOrWhiteSpace(userInfo.SessionKey))
        {
            await _telegramService.SendMessageAsync(chatId, TelegramMessages.Auth.LoginRequired, "HTML", ct);
            return;
        }

        // Check for active session
        if (userInfo.Conversation != null && 
            !userInfo.Conversation.IsExpired &&
            userInfo.Conversation.State != ConversationState.Idle &&
            userInfo.Conversation.State != ConversationState.AwaitingMealDescription)
        {
            await _telegramService.SendMessageAsync(chatId, TelegramMessages.Session.AlreadyActiveWithSave, null, ct);
            return;
        }

        await _telegramService.SendMessageAsync(chatId, TelegramMessages.Ocr.ProcessingPhoto, null, ct);

        try
        {
            var largestPhoto = photos.OrderByDescending(p => p.FileSize ?? 0).First();
            
            var fileResponse = await _telegramClient.GetFileAsync(
                new GetFileRequest { FileId = largestPhoto.FileId }, ct);

            if (fileResponse?.Ok != true || string.IsNullOrEmpty(fileResponse.Result?.FilePath))
            {
                await _telegramService.SendMessageAsync(chatId, TelegramMessages.Ocr.PhotoGetError, null, ct);
                return;
            }

            var imageBytes = await _telegramClient.DownloadFileAsync(fileResponse.Result.FilePath, ct);
            var extractedText = await _ocrService.ExtractTextFromImageAsync(imageBytes, ct);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                await _telegramService.SendMessageAsync(chatId, TelegramMessages.Ocr.NoTextDetected, null, ct);
                return;
            }

            _logger.LogInformation("[OCR] Extracted text for chatId {ChatId}: {Text}", chatId, extractedText);

            // Initialize or update conversation
            if (userInfo.Conversation == null || 
                userInfo.Conversation.IsExpired || 
                userInfo.Conversation.State == ConversationState.Idle)
            {
                userInfo.Conversation = new ConversationSession
                {
                    State = ConversationState.AwaitingOCRCorrection,
                    StartedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow,
                    MessageHistory = new List<ConversationMessage>()
                };
            }
            else
            {
                userInfo.Conversation.State = ConversationState.AwaitingOCRCorrection;
                userInfo.Conversation.Touch();
            }

            userInfo.Conversation.OcrExtractedText = extractedText;

            // Send detected text in code block (shows frame, easy to copy on mobile)
            await _telegramService.SendMessageAsync(chatId,
                TelegramMessages.Ocr.FormatDetectedTextOnly(extractedText), "Markdown", ct);
            
            // Send instructions separately
            await _telegramService.SendMessageAsync(chatId,
                TelegramMessages.Ocr.TextDetectedInstructions, "HTML", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing photo for chatId {ChatId}", chatId);
            await _telegramService.SendMessageAsync(chatId, TelegramMessages.Ocr.ProcessingOcrError, null, ct);
        }
    }
}
