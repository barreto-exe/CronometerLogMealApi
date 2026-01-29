using System.Collections.Concurrent;
using System.Text.Json;
using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Clients.OpenAIClient;
using CronometerLogMealApi.Clients.GeminiClient;
using CronometerLogMealApi.Clients.TelegramClient;
using CronometerLogMealApi.Clients.TelegramClient.Models;
using CronometerLogMealApi.Clients.TelegramClient.Requests;
using CronometerLogMealApi.Clients.AzureVisionClient;
using CronometerLogMealApi.Models;
using CronometerLogMealApi.Requests;

namespace CronometerLogMealApi.Services;

public class CronometerPollingHostedService : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(500);
    private readonly ConcurrentDictionary<string, CronometerUserInfo> _userSessions = new();

    private readonly ILogger<CronometerPollingHostedService> _logger;
    private readonly TelegramService _telegramService;
    private readonly TelegramHttpClient _telegramClient;
    private readonly CronometerHttpClient _cronometerClient;
    private readonly OpenAIHttpClient _openAIClient;
    private readonly CronometerService _cronometerService;
    private readonly AzureVisionService _azureVisionService;

    public CronometerPollingHostedService(
        ILogger<CronometerPollingHostedService> logger, 
        TelegramService service, 
        TelegramHttpClient telegramClient,
        CronometerHttpClient cronometerClient, 
        OpenAIHttpClient openAIClient, 
        CronometerService cronometerService,
        AzureVisionService azureVisionService)
    {
        _logger = logger;
        _telegramService = service;
        _telegramClient = telegramClient;
        _cronometerClient = cronometerClient;
        _openAIClient = openAIClient;
        _cronometerService = cronometerService;
        _azureVisionService = azureVisionService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure service is initialized
        await _telegramService.InitAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var res = await _telegramService.GetTelegramUpdates(null, stoppingToken);

                if (res?.Ok == true && res.Result is { Count: > 0 })
                {
                    foreach (var update in res.Result)
                    {
                        await HandleMessageAsync(update, stoppingToken);
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

    private async Task HandleMessageAsync(TelegramUpdate? update, CancellationToken ct)
    {
        if (update == null) return;

        var msg = update.Message ?? update.EditedMessage;
        var text = msg?.Text;
        var chatId = msg?.Chat?.Id;
        var photo = msg?.Photo;
        var caption = msg?.Caption;
        
        // Handle photo messages (OCR flow)
        if (photo != null && photo.Count > 0 && chatId.HasValue)
        {
            var chatIdStr = chatId.Value.ToString();
            await HandlePhotoMessageAsync(chatIdStr, photo, caption, ct);
            return;
        }
        
        if (!string.IsNullOrWhiteSpace(text) && chatId.HasValue)
        {
            var chatIdStr = chatId.Value.ToString();
            _logger.LogInformation("[Telegram] {ChatId}: {Text}", chatIdStr, text);

            // Check for session timeout
            if (_userSessions.TryGetValue(chatIdStr, out var existingUser) && 
                existingUser?.Conversation != null && 
                existingUser.Conversation.IsExpired)
            {
                _logger.LogInformation("Session expired for chatId {ChatId}", chatIdStr);
                existingUser.Conversation = null;
                await _telegramService.SendMessageAsync(chatIdStr, 
                    "⏰ Tu sesión anterior expiró por inactividad. Usa /start para iniciar una nueva.", 
                    null, ct);
            }

            // Handle commands first
            if (IsCommand(text, "/start"))
            {
                await HandleStartCommandAsync(chatIdStr, ct);
                return;
            }

            if (IsCommand(text, "/cancel") || IsCommand(text, "/cancelar"))
            {
                await HandleCancelCommandAsync(chatIdStr, ct);
                return;
            }

            if (IsLoginMessage(text))
            {
                await HandleLoginAsync(chatIdStr, text, ct);
                return;
            }

            if (IsCommand(text, "/save") || IsCommand(text, "/guardar"))
            {
                await HandleSaveCommandAsync(chatIdStr, ct);
                return;
            }

            if (IsCommand(text, "/continue") || IsCommand(text, "/continuar"))
            {
                await HandleContinueCommandAsync(chatIdStr, ct);
                return;
            }

            // Route based on conversation state
            await HandleConversationMessageAsync(chatIdStr, text, ct);
        }
    }

    private static bool IsCommand(string? text, string command)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Trim().StartsWith(command, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoginMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.Trim().ToLowerInvariant();
        return lower.Contains("login") || lower.Contains("log in") || lower.Contains("sign in");
    }

    private async Task HandlePhotoMessageAsync(string chatId, List<TelegramPhotoSize> photos, string? caption, CancellationToken ct)
    {
        // Check if user is logged in
        if (!_userSessions.TryGetValue(chatId, out var userInfo) || userInfo == null ||
            string.IsNullOrWhiteSpace(userInfo.SessionKey))
        {
            await _telegramService.SendMessageAsync(chatId,
                "⚠️ Primero debes iniciar sesión con:\n<b>/login &lt;email&gt; &lt;password&gt;</b>",
                "HTML", ct);
            return;
        }

        // Check if there's already an active session that is not expired
        if (userInfo.Conversation != null && !userInfo.Conversation.IsExpired &&
            userInfo.Conversation.State != ConversationState.Idle &&
            userInfo.Conversation.State != ConversationState.AwaitingMealDescription)
        {
            await _telegramService.SendMessageAsync(chatId,
                "⚠️ Ya tienes una sesión activa. Usa /cancel para cancelarla primero, o usa /save para guardar los cambios pendientes.",
                null, ct);
            return;
        }

        await _telegramService.SendMessageAsync(chatId, "📷 Procesando tu foto...", null, ct);

        try
        {
            // Get the largest photo (last in array)
            var largestPhoto = photos.OrderByDescending(p => p.FileSize ?? 0).First();
            
            // Get file info from Telegram
            var fileResponse = await _telegramClient.GetFileAsync(
                new GetFileRequest { FileId = largestPhoto.FileId }, ct);

            if (fileResponse?.Ok != true || string.IsNullOrEmpty(fileResponse.Result?.FilePath))
            {
                await _telegramService.SendMessageAsync(chatId,
                    "❌ No pude obtener la foto. Por favor, intenta enviarla de nuevo.",
                    null, ct);
                return;
            }

            // Download the file
            var imageBytes = await _telegramClient.DownloadFileAsync(fileResponse.Result.FilePath, ct);

            // Perform OCR
            var extractedText = await _azureVisionService.ExtractTextFromImageAsync(imageBytes, ct);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                await _telegramService.SendMessageAsync(chatId,
                    "❌ No pude leer texto en la imagen. Asegúrate de que el texto sea legible o envía un mensaje de texto describiendo tu comida.",
                    null, ct);
                return;
            }

            _logger.LogInformation("[OCR] Extracted text from photo for chatId {ChatId}: {Text}", chatId, extractedText);

            // Initialize or update conversation session
            if (userInfo.Conversation == null || userInfo.Conversation.IsExpired || 
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

            // Store OCR text for later use with corrections
            userInfo.Conversation.OcrExtractedText = extractedText;

            // Show extracted text and ask for corrections
            await _telegramService.SendMessageAsync(chatId,
                $"📝 <b>Texto detectado:</b>\n<i>{extractedText}</i>\n\n" +
                "✏️ Si hay algún error, escribe las correcciones.\n" +
                "✅ Si todo está correcto, usa /continue para continuar.",
                "HTML", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing photo for chatId {ChatId}", chatId);
            await _telegramService.SendMessageAsync(chatId,
                "❌ Ocurrió un error al procesar la imagen. Por favor, intenta de nuevo o envía un mensaje de texto.",
                null, ct);
        }
    }

    private async Task HandleStartCommandAsync(string chatId, CancellationToken ct)
    {
        if (!_userSessions.TryGetValue(chatId, out var userInfo) || userInfo == null ||
            string.IsNullOrWhiteSpace(userInfo.SessionKey))
        {
            await _telegramService.SendMessageAsync(chatId,
                "⚠️ Primero debes iniciar sesión con:\n<b>/login &lt;email&gt; &lt;password&gt;</b>",
                "HTML", ct);
            return;
        }

        // Check if there's already an active session
        if (userInfo.Conversation != null && !userInfo.Conversation.IsExpired &&
            userInfo.Conversation.State != ConversationState.Idle)
        {
            await _telegramService.SendMessageAsync(chatId,
                "⚠️ Ya tienes una sesión activa. Usa /cancel para cancelarla primero.",
                null, ct);
            return;
        }

        // Initialize new conversation session
        userInfo.Conversation = new ConversationSession
        {
            State = ConversationState.AwaitingMealDescription,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            MessageHistory = new List<ConversationMessage>()
        };

        await _telegramService.SendMessageAsync(chatId,
            "🍽️ <b>Nueva sesión de registro iniciada</b>\n\n" +
            "Describe tu comida incluyendo:\n" +
            "• 📅 Tipo de comida (desayuno, almuerzo, cena, merienda)\n" +
            "• ⚖️ Cantidades y pesos (ej: 100g de arroz)\n" +
            "• 📏 Tamaños cuando aplique (huevos pequeños, medianos, grandes)\n\n" +
            "💡 <i>Tip: Entre más detallado sea tu mensaje, menos preguntas tendré que hacerte.</i>\n\n" +
            "Usa /cancel para cancelar en cualquier momento.",
            "HTML", ct);
    }

    private async Task HandleCancelCommandAsync(string chatId, CancellationToken ct)
    {
        if (!_userSessions.TryGetValue(chatId, out var userInfo) || userInfo == null)
        {
            await _telegramService.SendMessageAsync(chatId,
                "No hay ninguna sesión activa para cancelar.",
                null, ct);
            return;
        }

        if (userInfo.Conversation == null || userInfo.Conversation.State == ConversationState.Idle)
        {
            await _telegramService.SendMessageAsync(chatId,
                "No hay ninguna sesión activa para cancelar.",
                null, ct);
            return;
        }

        userInfo.Conversation = null;

        await _telegramService.SendMessageAsync(chatId,
            "❌ Sesión cancelada. Usa /start para iniciar una nueva.",
            null, ct);
    }

    private async Task HandleContinueCommandAsync(string chatId, CancellationToken ct)
    {
        if (!_userSessions.TryGetValue(chatId, out var userInfo) || userInfo?.Conversation == null)
        {
            await _telegramService.SendMessageAsync(chatId,
                "No hay una sesión activa. Usa /start para iniciar.", null, ct);
            return;
        }

        if (userInfo.Conversation.State != ConversationState.AwaitingOCRCorrection)
        {
            await _telegramService.SendMessageAsync(chatId,
                "⚠️ Este comando solo se puede usar después de enviar una foto para confirmar el texto detectado.",
                null, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(userInfo.Conversation.OcrExtractedText))
        {
            await _telegramService.SendMessageAsync(chatId,
                "❌ No hay texto OCR guardado. Por favor, envía una foto nuevamente.",
                null, ct);
            userInfo.Conversation.State = ConversationState.Idle;
            return;
        }

        // Process OCR text without corrections
        await ProcessOCRTextAsync(chatId, userInfo, userInfo.Conversation.OcrExtractedText, null, ct);
    }

    private async Task HandleOCRCorrectionAsync(string chatId, CronometerUserInfo userInfo, string correctionText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userInfo.Conversation?.OcrExtractedText))
        {
            await _telegramService.SendMessageAsync(chatId,
                "❌ No hay texto OCR guardado. Por favor, envía una foto nuevamente.",
                null, ct);
            return;
        }

        // Process OCR text with user corrections
        await ProcessOCRTextAsync(chatId, userInfo, userInfo.Conversation.OcrExtractedText, correctionText, ct);
    }

    private async Task ProcessOCRTextAsync(string chatId, CronometerUserInfo userInfo, string ocrText, string? corrections, CancellationToken ct)
    {
        userInfo.Conversation!.State = ConversationState.Processing;
        await _telegramService.SendMessageAsync(chatId, "⏳ Procesando...", null, ct);

        try
        {
            // Build description with corrections if provided
            string fullDescription;
            if (!string.IsNullOrWhiteSpace(corrections))
            {
                fullDescription = $"{ocrText}\n\nCORRECCIONES DEL USUARIO: {corrections}";
                _logger.LogInformation("[OCR] Processing with corrections for chatId {ChatId}: {Corrections}", chatId, corrections);
            }
            else
            {
                fullDescription = ocrText;
            }

            // Add to conversation history
            userInfo.Conversation.OriginalDescription = fullDescription;
            userInfo.Conversation.MessageHistory.Add(new ConversationMessage
            {
                Role = "user",
                Content = fullDescription,
                Timestamp = DateTime.UtcNow
            });

            // Process as meal description
            var result = await ProcessMealDescriptionAsync(fullDescription, ct);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                userInfo.Conversation.State = ConversationState.AwaitingMealDescription;
                await _telegramService.SendMessageAsync(chatId,
                    $"❌ {result.ErrorMessage}\n\nPor favor, intenta describir tu comida nuevamente.",
                    null, ct);
                return;
            }

            if (result.NeedsClarification && result.Clarifications.Count > 0)
            {
                userInfo.Conversation.State = ConversationState.AwaitingClarification;
                userInfo.Conversation.PendingClarifications = result.Clarifications;
                userInfo.Conversation.PendingMealRequest = result.MealRequest;

                var clarificationMessage = FormatClarificationQuestions(result.Clarifications);

                userInfo.Conversation.MessageHistory.Add(new ConversationMessage
                {
                    Role = "assistant",
                    Content = clarificationMessage,
                    Timestamp = DateTime.UtcNow
                });

                await _telegramService.SendMessageAsync(chatId,
                    "🤔 Necesito un poco más de información:\n\n" + clarificationMessage,
                    "HTML", ct);
                return;
            }

            // No clarification needed, try to log the meal
            await AttemptMealLoggingAsync(chatId, userInfo, result.MealRequest!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OCR text for chatId {ChatId}", chatId);
            userInfo.Conversation.State = ConversationState.AwaitingOCRCorrection;
            await _telegramService.SendMessageAsync(chatId,
                "❌ Ocurrió un error al procesar. Por favor, intenta de nuevo.",
                null, ct);
        }
    }

    private async Task HandleConversationMessageAsync(string chatId, string text, CancellationToken ct)
    {
        if (!_userSessions.TryGetValue(chatId, out var userInfo) || userInfo == null)
        {
            await _telegramService.SendMessageAsync(chatId,
                "No estás autenticado. Por favor, inicia sesión usando el comando:\n" +
                "<b>/login &lt;email&gt; &lt;password&gt;</b>",
                "HTML", ct);
            return;
        }

        if (userInfo.Conversation == null || userInfo.Conversation.State == ConversationState.Idle)
        {
            await _telegramService.SendMessageAsync(chatId,
                "💡 Para registrar una comida, usa el comando /start para iniciar una nueva sesión.",
                null, ct);
            return;
        }

        // Update activity timestamp
        userInfo.Conversation.Touch();

        switch (userInfo.Conversation.State)
        {
            case ConversationState.AwaitingMealDescription:
                await HandleMealDescriptionAsync(chatId, userInfo, text, ct);
                break;

            case ConversationState.AwaitingClarification:
                await HandleClarificationResponseAsync(chatId, userInfo, text, ct);
                break;

            case ConversationState.AwaitingOCRCorrection:
                await HandleOCRCorrectionAsync(chatId, userInfo, text, ct);
                break;

            case ConversationState.AwaitingConfirmation:
                await HandleConfirmationResponseAsync(chatId, userInfo, text, ct);
                break;

            case ConversationState.Processing:
                await _telegramService.SendMessageAsync(chatId,
                    "⏳ Aún estoy procesando tu solicitud anterior. Por favor, espera un momento.",
                    null, ct);
                break;

            default:
                await _telegramService.SendMessageAsync(chatId,
                    "💡 Usa /start para iniciar una nueva sesión de registro.",
                    null, ct);
                break;
        }
    }

    private async Task HandleMealDescriptionAsync(string chatId, CronometerUserInfo userInfo, string text, CancellationToken ct)
    {
        // Store original description
        userInfo.Conversation!.OriginalDescription = text;

        // Add to conversation history
        userInfo.Conversation.MessageHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = text,
            Timestamp = DateTime.UtcNow
        });

        userInfo.Conversation.State = ConversationState.Processing;
        await _telegramService.SendMessageAsync(chatId, "⏳ Procesando tu mensaje...", null, ct);

        try
        {
            var result = await ProcessMealDescriptionAsync(text, ct);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                userInfo.Conversation.State = ConversationState.AwaitingMealDescription;
                await _telegramService.SendMessageAsync(chatId,
                    $"❌ {result.ErrorMessage}\n\nPor favor, intenta describir tu comida nuevamente.",
                    null, ct);
                return;
            }

            if (result.NeedsClarification && result.Clarifications.Count > 0)
            {
                userInfo.Conversation.State = ConversationState.AwaitingClarification;
                userInfo.Conversation.PendingClarifications = result.Clarifications;
                userInfo.Conversation.PendingMealRequest = result.MealRequest;

                var clarificationMessage = FormatClarificationQuestions(result.Clarifications);

                userInfo.Conversation.MessageHistory.Add(new ConversationMessage
                {
                    Role = "assistant",
                    Content = clarificationMessage,
                    Timestamp = DateTime.UtcNow
                });

                await _telegramService.SendMessageAsync(chatId,
                    "🤔 Necesito un poco más de información:\n\n" + clarificationMessage,
                    "HTML", ct);
                return;
            }

            // No clarification needed, try to log the meal
            await AttemptMealLoggingAsync(chatId, userInfo, result.MealRequest!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing meal description for chatId {ChatId}", chatId);
            userInfo.Conversation.State = ConversationState.AwaitingMealDescription;
            await _telegramService.SendMessageAsync(chatId,
                "❌ Ocurrió un error al procesar tu mensaje. Por favor, intenta nuevamente.",
                null, ct);
        }
    }

    private async Task HandleClarificationResponseAsync(string chatId, CronometerUserInfo userInfo, string text, CancellationToken ct)
    {
        // Add clarification to history
        userInfo.Conversation!.MessageHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = text,
            Timestamp = DateTime.UtcNow
        });

        userInfo.Conversation.State = ConversationState.Processing;
        await _telegramService.SendMessageAsync(chatId, "⏳ Procesando tu respuesta...", null, ct);

        try
        {
            // Build full context from conversation history
            var fullContext = BuildConversationContext(userInfo.Conversation.MessageHistory);

            var result = await ProcessMealDescriptionAsync(fullContext, ct);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                userInfo.Conversation.State = ConversationState.AwaitingClarification;
                await _telegramService.SendMessageAsync(chatId,
                    $"❌ {result.ErrorMessage}\n\nPor favor, intenta responder nuevamente.",
                    null, ct);
                return;
            }

            if (result.NeedsClarification && result.Clarifications.Count > 0)
            {
                // Still needs more clarification
                userInfo.Conversation.State = ConversationState.AwaitingClarification;
                userInfo.Conversation.PendingClarifications = result.Clarifications;
                userInfo.Conversation.PendingMealRequest = result.MealRequest;

                var clarificationMessage = FormatClarificationQuestions(result.Clarifications);

                userInfo.Conversation.MessageHistory.Add(new ConversationMessage
                {
                    Role = "assistant",
                    Content = clarificationMessage,
                    Timestamp = DateTime.UtcNow
                });

                await _telegramService.SendMessageAsync(chatId,
                    "🤔 Aún necesito más información:\n\n" + clarificationMessage,
                    "HTML", ct);
                return;
            }

            // All clarified, try to log
            await AttemptMealLoggingAsync(chatId, userInfo, result.MealRequest!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing clarification for chatId {ChatId}", chatId);
            userInfo.Conversation.State = ConversationState.AwaitingClarification;
            await _telegramService.SendMessageAsync(chatId,
                "❌ Ocurrió un error al procesar tu respuesta. Por favor, intenta nuevamente.",
                null, ct);
        }
    }

    private async Task HandleConfirmationResponseAsync(string chatId, CronometerUserInfo userInfo, string text, CancellationToken ct)
    {
        // If user didn't use /save but sent text, assume they want to change something
        // Treat as a new description or clarification
        await _telegramService.SendMessageAsync(chatId, 
            "🔄 Entendido, vamos a corregir. Procesando tus cambios...", null, ct);
            
        // Reset valid outcome and go back to processing logic
        // We add the new text to history to contextually update the request
        userInfo.Conversation!.MessageHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = text,
            Timestamp = DateTime.UtcNow
        });

        userInfo.Conversation.State = ConversationState.Processing;

        try
        {
            var fullContext = BuildConversationContext(userInfo.Conversation.MessageHistory);
            var result = await ProcessMealDescriptionAsync(fullContext, ct);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                userInfo.Conversation.State = ConversationState.AwaitingMealDescription; // Or should we stay in confirmation but failed? 
                                                                                         // Better to restart flow or ask purely about the error?
                                                                                         // Let's go to awaiting description to be safe.
                await _telegramService.SendMessageAsync(chatId,
                    $"❌ {result.ErrorMessage}\n\nPor favor, intenta describir tu comida nuevamente.",
                    null, ct);
                return;
            }

            if (result.NeedsClarification)
            {
                userInfo.Conversation.State = ConversationState.AwaitingClarification;
                userInfo.Conversation.PendingClarifications = result.Clarifications;
                userInfo.Conversation.PendingMealRequest = result.MealRequest;

                var clarificationMessage = FormatClarificationQuestions(result.Clarifications);
                 userInfo.Conversation.MessageHistory.Add(new ConversationMessage
                {
                    Role = "assistant",
                    Content = clarificationMessage,
                    Timestamp = DateTime.UtcNow
                });

                await _telegramService.SendMessageAsync(chatId,
                    "🤔 Necesito un poco más de información:\n\n" + clarificationMessage,
                    "HTML", ct);
                return;
            }

            // If clear, validate again
            await AttemptMealLoggingAsync(chatId, userInfo, result.MealRequest!, ct);
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Error processing change request for chatId {ChatId}", chatId);
            await _telegramService.SendMessageAsync(chatId,
                "❌ Ocurrió un error al procesar tu cambio. Intenta nuevamente.",
                null, ct);
             // Revert to confirmation state so they don't lose progress? 
             // actually safe to just leave them be, or maybe reset state?
             // Let's leave state as processing -> failed, so maybe go back to AwaitingConfirmation
             userInfo.Conversation.State = ConversationState.AwaitingConfirmation;
        }
    }

    private async Task HandleSaveCommandAsync(string chatId, CancellationToken ct)
    {
        if (!_userSessions.TryGetValue(chatId, out var userInfo) || userInfo?.Conversation == null)
        {
             await _telegramService.SendMessageAsync(chatId,
                "No hay una sesión activa para guardar.", null, ct);
            return;
        }

        if (userInfo.Conversation.State != ConversationState.AwaitingConfirmation)
        {
             await _telegramService.SendMessageAsync(chatId,
                "⚠️ No hay cambios pendientes de confirmación. Usa /start para iniciar.", null, ct);
            return;
        }

        if (userInfo.Conversation.PendingMealRequest == null || !userInfo.Conversation.ValidatedFoods.Any())
        {
             await _telegramService.SendMessageAsync(chatId,
                "❌ Error interno: No hay datos de comida validados. Por favor inicia de nuevo con /start.", null, ct);
             userInfo.Conversation.State = ConversationState.Idle;
            return;
        }

        // Proceed to save to Cronometer using VALIDATED items
        await _telegramService.SendMessageAsync(chatId, "💾 Guardando cambios...", null, ct);

        try
        {
            var request = userInfo.Conversation.PendingMealRequest;
            
            // Log order based on category
            int order = request.Category.ToLower() switch
            {
                "breakfast" => 65537,
                "lunch" => 131073,
                "dinner" => 196609,
                "snacks" => 262145,
                _ => 1
            };

            var servingPayload = new AddMultiServingRequest
            {
                Servings = new List<ServingPayload>(),
                Auth = new AuthPayload { UserId = userInfo.UserId!.Value, Token = userInfo.SessionKey! }
            };

            foreach (var item in userInfo.Conversation.ValidatedFoods)
            {
                // Calculate the correct grams value based on whether it's raw grams or measure-based
                double grams;
                if (item.IsRawGrams)
                {
                    // User specified raw grams directly - use quantity as the gram value
                    grams = item.Quantity;
                }
                else
                {
                    // Normal measure-based calculation
                    grams = item.Quantity * item.MeasureGrams;
                }

                servingPayload.Servings.Add(new ServingPayload
                {
                    Order = order,
                    Day = request.Date.ToString("yyyy-MM-dd"),
                    Time = request.LogTime == true ? request.Date.ToString("HH:m:s") : string.Empty,
                    UserId = userInfo.UserId!.Value,
                    Type = "Serving",
                    FoodId = item.FoodId,
                    MeasureId = item.MeasureId,
                    Grams = grams
                });
            }

            var result = await _cronometerClient.AddMultiServingAsync(servingPayload, ct);

            // Assuming success if no exception and result is ok (simplified, existing code had more checks)
            // Ideally we check implicit success similar to CronometerService logic
             bool hasFailed = result != null &&
                result.Raw.ValueKind == JsonValueKind.Object &&
                result.Raw.TryGetProperty("result", out var resultProp) &&
                string.Equals(resultProp.GetString(), "fail", StringComparison.OrdinalIgnoreCase);

            if (hasFailed)
            {
                 await _telegramService.SendMessageAsync(chatId, "❌ Error al guardar en Cronometer.", null, ct);
                 return;
            }

            // Success
            userInfo.Conversation = null; // Clear session

            await _telegramService.SendMessageAsync(chatId,
                "✅ <b>¡Guardado exitoso!</b>\n\nTu comida ha sido registrada.",
                "HTML", ct);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving meal for chatId {ChatId}", chatId);
             await _telegramService.SendMessageAsync(chatId,
                "❌ Ocurrió un error al guardar. Intenta /save nuevamente.", null, ct);
        }
    }
    private async Task AttemptMealLoggingAsync(string chatId, CronometerUserInfo userInfo, LogMealRequest request, CancellationToken ct)
    {
        await _telegramService.SendMessageAsync(chatId, "🔍 Validando con Cronometer...", null, ct);

        // Perform validation instead of direct logging
        var (validatedItems, notFoundItems) = await _cronometerService.ValidateMealItemsAsync(
            request.Items,
            new AuthPayload { UserId = userInfo.UserId!.Value, Token = userInfo.SessionKey! },
            ct);

        if (notFoundItems.Count > 0)
        {
            userInfo.Conversation!.State = ConversationState.AwaitingClarification;
            var notFoundList = string.Join("\n", notFoundItems.Select(i => $"• <b>{i}</b>"));

            userInfo.Conversation.PendingClarifications = notFoundItems
                .Select(item => new ClarificationItem
                {
                    Type = ClarificationType.FoodNotFound,
                    ItemName = item,
                    Question = $"¿Podrías darme un nombre alternativo para \"{item}\"?"
                })
                .ToList();

             await _telegramService.SendMessageAsync(chatId,
                "⚠️ <b>No encontré estos alimentos:</b>\n\n" +
                notFoundList + "\n\n" +
                "Por favor, dame nombres alternativos (ej: \"pollo\" -> \"pechuga de pollo\").",
                "HTML", ct);
            return;
        }

        // All items validated successfully
        userInfo.Conversation!.PendingMealRequest = request;
        userInfo.Conversation.ValidatedFoods = validatedItems;
        userInfo.Conversation.State = ConversationState.AwaitingConfirmation;

        // Build summary message - use DisplayQuantity for proper formatting
        var itemsSummary = string.Join("\n", validatedItems.Select(i => 
            $"• {i.DisplayQuantity} de <b>{i.FoodName}</b>"));

        var msg = $"💾 Estás a punto de registrar:\n\n" +
                  $"<b>Hora:</b> {request.Date:h:mm tt}\n" +
                  $"<b>Tipo:</b> {request.Category.ToUpper()}\n\n" +
                  $"<b>Alimentos:</b>\n{itemsSummary}\n\n" +
                  $"¿Deseas hacer algún cambio? Si todo está bien, guarda los cambios con el comando <b>/save</b>.";

        await _telegramService.SendMessageAsync(chatId, msg, "HTML", ct);
    }

    private async Task<MealProcessingResult> ProcessMealDescriptionAsync(string text, CancellationToken ct)
    {
        // Get Venezuela time
        var venezuelaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Venezuela Standard Time");
        var venezuelaNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, venezuelaTimeZone);

        var prompt = GeminiPrompts.CronometerPrompt;
        prompt = prompt.Replace("@Now", venezuelaNow.ToString("yyyy-MM-ddTHH:mm:ss"));
        prompt = prompt.Replace("@UserInput", text);

        var openAIResponse = await _openAIClient.GenerateTextAsync(prompt, ct);
        var foodInfo = openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(foodInfo))
        {
            return MealProcessingResult.Failed("No se pudo procesar el mensaje.");
        }

        _logger.LogInformation("LLM response: {Response}", foodInfo);

        var cleanedJson = RemoveMarkdown(foodInfo);

        try
        {
            var response = JsonSerializer.Deserialize<LogMealRequestWithClarifications>(cleanedJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (response == null)
            {
                return MealProcessingResult.Failed("No se pudo interpretar la respuesta.");
            }

            // Check for error response
            if (!string.IsNullOrEmpty(response.Error))
            {
                return MealProcessingResult.Failed("No se pudo extraer información de comida del mensaje.");
            }

            // Convert LLM clarifications to our model
            var clarifications = response.Clarifications?
                .Select(c => new ClarificationItem
                {
                    Type = ParseClarificationType(c.Type),
                    ItemName = c.ItemName,
                    Question = c.Question
                })
                .ToList() ?? new List<ClarificationItem>();

            if (response.NeedsClarification && clarifications.Count > 0)
            {
                return MealProcessingResult.RequiresClarification(
                    new LogMealRequest
                    {
                        Category = response.Category,
                        Date = response.Date,
                        Items = response.Items,
                        LogTime = response.LogTime
                    },
                    clarifications);
            }

            return MealProcessingResult.Successful(new LogMealRequest
            {
                Category = response.Category,
                Date = response.Date,
                Items = response.Items,
                LogTime = response.LogTime
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM response as JSON: {Response}", cleanedJson);
            return MealProcessingResult.Failed("Error al procesar la respuesta del asistente.");
        }
    }

    private static ClarificationType ParseClarificationType(string type)
    {
        return type?.ToUpperInvariant() switch
        {
            "MISSING_SIZE" => ClarificationType.MissingSize,
            "MISSING_WEIGHT" => ClarificationType.MissingWeight,
            "AMBIGUOUS_UNIT" => ClarificationType.AmbiguousUnit,
            "UNCLEAR_FOOD" => ClarificationType.FoodNotFound,
            _ => ClarificationType.MissingWeight
        };
    }

    private static string FormatClarificationQuestions(List<ClarificationItem> clarifications)
    {
        if (clarifications.Count == 1)
        {
            return clarifications[0].Question;
        }

        var questions = clarifications.Select((c, i) => $"{i + 1}. {c.Question}");
        return string.Join("\n", questions);
    }

    private static string BuildConversationContext(List<ConversationMessage> history)
    {
        // Build a comprehensive context from all messages
        var userMessages = history
            .Where(m => m.Role == "user")
            .Select(m => m.Content);

        return string.Join(". ", userMessages);
    }

    private async Task HandleLoginAsync(string chatId, string text, CancellationToken ct)
    {
        //Validate that split text has at least 3 parts
        if (string.IsNullOrWhiteSpace(chatId) ||
            !text.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            text.Split(' ').Length < 3)
        {
            var reply = "Formato de logueo inválido. Use: /login <email> <password>";
            await _telegramService.SendMessageAsync(chatId, reply, null, ct);
            return;
        }

        // Extract email and password from the message
        var email = text.Split(' ').LastOrDefault(t => t.Contains('@'));
        var password = text.Split(' ')[2].Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            var reply = "Formato de logueo inválido. Use: <b>/login &lt;email&gt; &lt;password&gt;</b>";
            await _telegramService.SendMessageAsync(chatId, reply, null, ct);
            return;
        }

        // Proceed with login logic
        await _telegramService.SendMessageAsync(chatId, "🔐 Iniciando sesión...", null, ct);

        var loginResponse = await _cronometerClient.LoginAsync(new(email, password), ct);
        if (loginResponse.Result == "FAIL")
        {
            var reply = "❌ Error de autenticación. Por favor, verifique sus credenciales.";
            await _telegramService.SendMessageAsync(chatId, reply, null, ct);
            return;
        }

        var userInfo = new CronometerUserInfo
        {
            Email = email,
            Password = password,
            UserId = loginResponse.Id,
            SessionKey = loginResponse.SessionKey,
        };

        _userSessions.AddOrUpdate(chatId, userInfo, (key, oldValue) => userInfo);

        var successReply =
            "✅ <b>Inicio de sesión exitoso.</b>\n\n" +
            "Ahora puedes registrar tus comidas usando el comando /start.";
        await _telegramService.SendMessageAsync(chatId, successReply, "HTML", ct);
    }

    private string RemoveMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        if (text.StartsWith("```json"))
        {
            text = text.Substring("```json".Length);
        }
        if (text.StartsWith("```"))
        {
            text = text.Substring("```".Length);
        }
        if (text.EndsWith("```"))
        {
            text = text.Substring(0, text.Length - "```".Length);
        }

        var noBold = text.Replace("**", "").Replace("__", "");
        var noItalics = noBold.Replace("*", "").Replace("_", "");
        var noInlineCode = noItalics.Replace("`", "");
        var noCodeBlock = noInlineCode.Replace("```", "");
        return noCodeBlock.Trim();
    }
}
