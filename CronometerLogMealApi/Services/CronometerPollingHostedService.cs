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
using CronometerLogMealApi.Models.UserMemory;
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
    private readonly UserMemoryService? _memoryService;

    public CronometerPollingHostedService(
        ILogger<CronometerPollingHostedService> logger, 
        TelegramService service, 
        TelegramHttpClient telegramClient,
        CronometerHttpClient cronometerClient, 
        OpenAIHttpClient openAIClient, 
        CronometerService cronometerService,
        AzureVisionService azureVisionService,
        UserMemoryService? memoryService = null)
    {
        _logger = logger;
        _telegramService = service;
        _telegramClient = telegramClient;
        _cronometerClient = cronometerClient;
        _openAIClient = openAIClient;
        _cronometerService = cronometerService;
        _azureVisionService = azureVisionService;
        _memoryService = memoryService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure service is initialized
        await _telegramService.InitAsync(stoppingToken);

        // Restore sessions from Firestore
        await RestoreSessionsFromFirestoreAsync(stoppingToken);

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

    /// <summary>
    /// Restores user sessions from Firestore on server startup.
    /// </summary>
    private async Task RestoreSessionsFromFirestoreAsync(CancellationToken ct)
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

                _userSessions.AddOrUpdate(session.TelegramChatId, userInfo, (key, oldValue) => userInfo);
                _logger.LogInformation("Restored session for Telegram user {TelegramChatId} ({Email})",
                    session.TelegramChatId, session.Email);
            }

            _logger.LogInformation("Restored {Count} user sessions from Firestore", sessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring sessions from Firestore");
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

            if (IsCommand(text, "/preferences") || IsCommand(text, "/preferencias"))
            {
                await HandlePreferencesCommandAsync(chatIdStr, ct);
                return;
            }

            if (IsCommand(text, "/search") || IsCommand(text, "/buscar"))
            {
                await HandleSearchCommandAsync(chatIdStr, text, ct);
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
                // Enrich clarifications with original terms from the description
                EnrichClarificationsWithOriginalTerms(result.Clarifications, fullDescription);
                
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

            case ConversationState.AwaitingMemoryConfirmation:
                await HandleMemoryConfirmationResponseAsync(chatId, userInfo, text, ct);
                break;

            case ConversationState.AwaitingPreferenceAction:
                await HandlePreferenceActionResponseAsync(chatId, userInfo, text, ct);
                break;

            case ConversationState.AwaitingAliasInput:
                await HandleAliasInputResponseAsync(chatId, userInfo, text, ct);
                break;

            case ConversationState.AwaitingFoodSearch:
                await HandleFoodSearchResponseAsync(chatId, userInfo, text, ct);
                break;

            case ConversationState.AwaitingFoodSelection:
                await HandleFoodSelectionResponseAsync(chatId, userInfo, text, ct);
                break;

            case ConversationState.AwaitingAliasDeleteConfirm:
                await HandleAliasDeleteConfirmResponseAsync(chatId, userInfo, text, ct);
                break;

            case ConversationState.AwaitingFoodSearchSelection:
                await HandleFoodSearchSelectionResponseAsync(chatId, userInfo, text, ct);
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
            // STEP 1: Detect aliases in the user's text BEFORE sending to LLM
            var textForLlm = text;
            if (_memoryService != null)
            {
                var detectedAliases = await _memoryService.DetectAliasesInTextAsync(chatId, text, ct);
                userInfo.Conversation.DetectedAliases = detectedAliases;
                
                if (detectedAliases.Count > 0)
                {
                    _logger.LogInformation("Pre-detected {Count} aliases in user input: [{Aliases}]",
                        detectedAliases.Count,
                        string.Join(", ", detectedAliases.Select(a => $"'{a.Alias.InputTerm}' -> '{a.Alias.ResolvedFoodName}'")));
                    
                    // STEP 1.5: Replace aliases in text with their resolved names for LLM processing
                    // This prevents LLM from asking "what type of X?" for known aliases
                    textForLlm = ReplaceAliasesInText(text, detectedAliases);
                    _logger.LogInformation("Text after alias replacement: '{TextForLlm}'", textForLlm);
                }
            }

            // STEP 2: Send to LLM for parsing (with aliases replaced)
            var result = await ProcessMealDescriptionAsync(textForLlm, ct);

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
                // Enrich clarifications with original terms from user input
                EnrichClarificationsWithOriginalTerms(result.Clarifications, text);
                
                // Log the enriched clarifications for debugging
                foreach (var c in result.Clarifications)
                {
                    _logger.LogInformation("Clarification: ItemName='{ItemName}', OriginalTerm='{OriginalTerm}', Type={Type}",
                        c.ItemName, c.OriginalTerm, c.Type);
                }

                // Check if we have saved preferences for any clarifications
                var remainingClarifications = new List<ClarificationItem>();
                var autoAppliedAnswers = new List<string>();

                if (_memoryService != null)
                {
                    foreach (var clarification in result.Clarifications)
                    {
                        var termToCheck = !string.IsNullOrEmpty(clarification.OriginalTerm) 
                            ? clarification.OriginalTerm 
                            : clarification.ItemName;
                        
                        _logger.LogInformation("Checking clarification preference for term: '{Term}', type: '{Type}'",
                            termToCheck, clarification.Type.ToString());

                        var preference = await _memoryService.FindClarificationPreferenceAsync(
                            chatId, termToCheck, clarification.Type.ToString(), ct);

                        if (preference != null)
                        {
                            // Auto-apply the preference
                            autoAppliedAnswers.Add($"{termToCheck} -> {preference.DefaultAnswer}");
                            _logger.LogInformation("🧠 Auto-applying clarification preference: '{Term}' + {Type} -> '{Answer}'",
                                termToCheck, clarification.Type, preference.DefaultAnswer);
                            
                            // Add the answer to conversation history so LLM uses it
                            userInfo.Conversation.MessageHistory.Add(new ConversationMessage
                            {
                                Role = "user",
                                Content = preference.DefaultAnswer,
                                Timestamp = DateTime.UtcNow
                            });
                        }
                        else
                        {
                            remainingClarifications.Add(clarification);
                        }
                    }
                }
                else
                {
                    remainingClarifications = result.Clarifications;
                }

                // If all clarifications were auto-applied, re-process
                if (remainingClarifications.Count == 0 && autoAppliedAnswers.Count > 0)
                {
                    await _telegramService.SendMessageAsync(chatId,
                        $"🧠 Usando tus preferencias guardadas ({string.Join(", ", autoAppliedAnswers)})...",
                        null, ct);

                    // Re-process with the added answers
                    var fullContext = BuildConversationContext(userInfo.Conversation.MessageHistory);
                    var retryResult = await ProcessMealDescriptionAsync(fullContext, ct);

                    if (!retryResult.NeedsClarification && retryResult.MealRequest != null)
                    {
                        await AttemptMealLoggingAsync(chatId, userInfo, retryResult.MealRequest, ct);
                        return;
                    }
                    // If still needs clarification, fall through to ask
                    // Re-enrich the clarifications with original terms
                    EnrichClarificationsWithOriginalTerms(retryResult.Clarifications, text);
                    remainingClarifications = retryResult.Clarifications;
                }

                if (remainingClarifications.Count > 0)
                {
                    // Re-enrich remaining clarifications to ensure OriginalTerm is set
                    EnrichClarificationsWithOriginalTerms(remainingClarifications, text);
                    
                    userInfo.Conversation.State = ConversationState.AwaitingClarification;
                    userInfo.Conversation.PendingClarifications = remainingClarifications;
                    userInfo.Conversation.PendingMealRequest = result.MealRequest;

                    var clarificationMessage = FormatClarificationQuestions(remainingClarifications);

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

        // Record clarification pattern for learning (if we have pending clarifications)
        if (_memoryService != null && userInfo.Conversation.PendingClarifications.Count > 0)
        {
            _logger.LogInformation("Recording clarification patterns. Pending clarifications: {Count}", 
                userInfo.Conversation.PendingClarifications.Count);
            
            // Parse the user's response to map each answer to its corresponding clarification
            var parsedResponses = ParseClarificationResponses(text, userInfo.Conversation.PendingClarifications);
            
            _logger.LogInformation("Parsed {ParsedCount} responses from user input for {TotalCount} clarifications",
                parsedResponses.Count, userInfo.Conversation.PendingClarifications.Count);
            
            // Only record patterns when we can clearly associate a response with a clarification
            foreach (var (clarification, answer) in parsedResponses)
            {
                var termToRecord = !string.IsNullOrEmpty(clarification.OriginalTerm)
                    ? clarification.OriginalTerm
                    : clarification.ItemName;

                _logger.LogInformation("Recording pattern: Term='{Term}', Type='{Type}', Answer='{Answer}'",
                    termToRecord, clarification.Type.ToString(), answer);

                var wasConfirmed = await _memoryService.RecordClarificationPatternAsync(
                    chatId,
                    termToRecord,
                    clarification.Type.ToString(),
                    answer,
                    ct);

                if (wasConfirmed)
                {
                    _logger.LogInformation("🧠 New clarification preference confirmed: '{Term}' + {Type} -> '{Answer}'",
                        termToRecord, clarification.Type, answer);
                }
            }
        }
        else
        {
            _logger.LogWarning("Not recording clarification patterns. MemoryService: {HasMemory}, PendingClarifications: {Count}",
                _memoryService != null, userInfo.Conversation.PendingClarifications?.Count ?? 0);
        }

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
                // Still needs more clarification - enrich with original terms from conversation history
                var originalInput = GetOriginalMealDescriptionFromHistory(userInfo.Conversation.MessageHistory);
                EnrichClarificationsWithOriginalTerms(result.Clarifications, originalInput);
                
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
        // Check if user wants to search alternatives for a specific item (responds with number)
        if (int.TryParse(text.Trim(), out int itemIndex) && 
            userInfo.Conversation?.ValidatedFoods != null &&
            itemIndex >= 1 && itemIndex <= userInfo.Conversation.ValidatedFoods.Count)
        {
            // User wants to search alternatives for item at index
            await HandleAlternativeSearchAsync(chatId, userInfo, itemIndex - 1, ct);
            return;
        }

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
                // Enrich clarifications with original terms from conversation history
                var originalInput = GetOriginalMealDescriptionFromHistory(userInfo.Conversation.MessageHistory);
                EnrichClarificationsWithOriginalTerms(result.Clarifications, originalInput);
                
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
            var pendingLearnings = userInfo.Conversation.PendingLearnings;
            
            // Check if there are learnings to save and memory service is available
            if (_memoryService != null && pendingLearnings.Count > 0)
            {
                // Ask user if they want to remember these mappings
                userInfo.Conversation.State = ConversationState.AwaitingMemoryConfirmation;

                var learningsMsg = "✅ <b>¡Guardado exitoso!</b>\n\n" +
                    "🧠 <b>¿Quieres que recuerde estas asociaciones?</b>\n\n" +
                    string.Join("\n", pendingLearnings.Select((l, i) => 
                        $"{i + 1}. \"{l.OriginalTerm}\" → <b>{l.ResolvedFoodName}</b>")) +
                    "\n\n• Responde <b>si</b> para guardar todas\n" +
                    "• Responde con los números (ej: 1,3) para guardar solo algunas\n" +
                    "• Responde <b>no</b> para no guardar ninguna";

                await _telegramService.SendMessageAsync(chatId, learningsMsg, "HTML", ct);
            }
            else
            {
                // No learnings to save, just finish
                userInfo.Conversation = null;
                await _telegramService.SendMessageAsync(chatId,
                    "✅ <b>¡Guardado exitoso!</b>\n\nTu comida ha sido registrada.",
                    "HTML", ct);
            }

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

        // Perform validation with memory support and pre-detected aliases
        var (validatedItems, notFoundItems) = await _cronometerService.ValidateMealItemsAsync(
            request.Items,
            new AuthPayload { UserId = userInfo.UserId!.Value, Token = userInfo.SessionKey! },
            chatId, // Pass userId for memory lookup
            userInfo.Conversation?.DetectedAliases, // Pass pre-detected aliases
            userInfo.Conversation?.OriginalDescription, // Pass original text for alias matching
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
                "Por favor, dame nombres alternativos (ej: \"pollo\" -> \"pechuga de pollo\").\n\n" +
                "💡 Tip: Usa /search [nombre] para buscar manualmente.",
                "HTML", ct);
            return;
        }

        // All items validated successfully
        userInfo.Conversation!.PendingMealRequest = request;
        userInfo.Conversation.ValidatedFoods = validatedItems;
        userInfo.Conversation.State = ConversationState.AwaitingConfirmation;

        // Clear previous pending learnings - only add when user explicitly searches for alternatives
        // We don't auto-suggest learnings for translated terms (e.g., "huevos" -> "Eggs")
        // because that's just the LLM doing translation, not user preference
        userInfo.Conversation.PendingLearnings.Clear();

        // Build summary message
        var itemsSummary = string.Join("\n", validatedItems.Select((item, idx) =>
        {
            var aliasIndicator = item.WasResolvedFromAlias ? " 🧠" : "";
            return $"{idx + 1}. {item.DisplayQuantity} de <b>{item.FoodName}</b>{aliasIndicator}";
        }));

        var hasMemoryItems = validatedItems.Any(v => v.WasResolvedFromAlias);
        var memoryLegend = hasMemoryItems ? "🧠 = reconocido desde tu memoria\n\n" : "";

        var msg = $"💾 Estás a punto de registrar:\n\n" +
                  $"<b>Hora:</b> {request.Date:h:mm tt}\n" +
                  $"<b>Tipo:</b> {request.Category.ToUpper()}\n\n" +
                  $"<b>Alimentos:</b>\n{itemsSummary}\n\n" +
                  memoryLegend +
                  "¿Deseas hacer algún cambio?\n" +
                  "• Responde con el número del item para <b>buscar alternativas</b>\n" +
                  "• Usa <b>/save</b> para guardar los cambios";

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
                .Select(c => 
                {
                    _logger.LogInformation("Parsing clarification type: raw='{RawType}'", c.Type);
                    return new ClarificationItem
                    {
                        Type = ParseClarificationType(c.Type),
                        ItemName = c.ItemName,
                        Question = c.Question
                    };
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
        // Normalize: remove underscores and convert to uppercase
        var normalizedType = type?.Replace("_", "").ToUpperInvariant();
        
        return normalizedType switch
        {
            "MISSINGSIZE" => ClarificationType.MissingSize,
            "MISSINGWEIGHT" => ClarificationType.MissingWeight,
            "AMBIGUOUSUNIT" => ClarificationType.AmbiguousUnit,
            "UNCLEARFOOD" => ClarificationType.FoodNotFound,
            "FOODNOTFOUND" => ClarificationType.FoodNotFound,
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

    /// <summary>
    /// Parses user's response to clarification questions and maps each answer to its corresponding clarification.
    /// Supports formats like: "1. grande 2. 200g" or "grande, 200g" or just "grande" (single clarification)
    /// </summary>
    private Dictionary<ClarificationItem, string> ParseClarificationResponses(
        string userResponse, 
        List<ClarificationItem> pendingClarifications)
    {
        var result = new Dictionary<ClarificationItem, string>();
        
        if (string.IsNullOrWhiteSpace(userResponse) || pendingClarifications.Count == 0)
            return result;

        var response = userResponse.Trim();
        
        // Case 1: Single clarification - the entire response is the answer
        if (pendingClarifications.Count == 1)
        {
            result[pendingClarifications[0]] = response;
            _logger.LogDebug("Single clarification, mapped entire response: '{Response}'", response);
            return result;
        }

        // Case 2: Try newline separated responses first (most common for multiple clarifications)
        // User often responds with:
        // 1. grandes
        // 2. 100g
        var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count >= pendingClarifications.Count)
        {
            // Try to parse each line, removing number prefix if present
            for (int i = 0; i < pendingClarifications.Count && i < lines.Count; i++)
            {
                var line = lines[i];
                // Remove numbered prefix like "1. ", "1) ", "1: "
                var cleanedLine = System.Text.RegularExpressions.Regex.Replace(line, @"^\d+[\.\)\:]\s*", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanedLine))
                {
                    result[pendingClarifications[i]] = cleanedLine;
                    _logger.LogDebug("Parsed line response {Index}: '{Answer}'", i + 1, cleanedLine);
                }
            }
            
            if (result.Count == pendingClarifications.Count)
                return result;
            
            // Reset if we didn't get all
            result.Clear();
        }

        // Case 3: Try single-line numbered responses  
        // Format: "1. grande 2. 200g" or "1) grande 2) 200g" or "1: grande 2: 200g"
        // Improved regex: capture everything until the next number-dot/paren/colon or end of string
        var numberedPattern = new System.Text.RegularExpressions.Regex(
            @"(\d+)[\.\)\:]\s*(.+?)(?=\s+\d+[\.\)\:]|$)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        var matches = numberedPattern.Matches(response);
        
        if (matches.Count > 0)
        {
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (int.TryParse(match.Groups[1].Value, out int num) && 
                    num >= 1 && num <= pendingClarifications.Count)
                {
                    var answer = match.Groups[2].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(answer))
                    {
                        result[pendingClarifications[num - 1]] = answer;
                        _logger.LogDebug("Parsed numbered response {Num}: '{Answer}'", num, answer);
                    }
                }
            }
            
            if (result.Count > 0)
                return result;
        }

        // Case 4: Try comma/semicolon separated responses
        var separators = new[] { ',', ';', '\n' };
        var parts = response.Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (parts.Count == pendingClarifications.Count)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                result[pendingClarifications[i]] = parts[i];
                _logger.LogDebug("Parsed separated response {Index}: '{Answer}'", i + 1, parts[i]);
            }
            return result;
        }

        // Case 4: Try to match responses based on clarification type keywords
        foreach (var clarification in pendingClarifications)
        {
            var matchedAnswer = TryExtractAnswerForClarificationType(response, clarification);
            if (!string.IsNullOrEmpty(matchedAnswer))
            {
                result[clarification] = matchedAnswer;
                _logger.LogDebug("Matched by type {Type}: '{Answer}'", clarification.Type, matchedAnswer);
            }
        }

        // Case 5: If we still couldn't parse, and there's only one answer-like segment
        // Don't record anything - ambiguous response
        if (result.Count == 0)
        {
            _logger.LogWarning("Could not parse clarification response: '{Response}' for {Count} clarifications",
                response, pendingClarifications.Count);
        }

        return result;
    }

    /// <summary>
    /// Attempts to extract an answer for a specific clarification type from the response.
    /// </summary>
    private static string? TryExtractAnswerForClarificationType(string response, ClarificationItem clarification)
    {
        var responseLower = response.ToLowerInvariant();
        
        switch (clarification.Type)
        {
            case ClarificationType.MissingSize:
                // Look for size keywords: pequeño, mediano, grande, chico, regular
                var sizeKeywords = new[] { "pequeño", "pequeña", "chico", "chica", "mediano", "mediana", "regular", "grande", "extra grande", "xl", "small", "medium", "large" };
                foreach (var keyword in sizeKeywords)
                {
                    if (responseLower.Contains(keyword))
                        return keyword;
                }
                break;

            case ClarificationType.MissingWeight:
                // Look for weight/quantity patterns: 200g, 1 taza, 100ml, etc.
                var weightPattern = new System.Text.RegularExpressions.Regex(
                    @"(\d+(?:\.\d+)?\s*(?:g|gr|gramos?|kg|ml|l|litros?|oz|onzas?|lb|libras?|tazas?|cucharadas?|cdas?|cups?|tbsp|tsp))",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var match = weightPattern.Match(response);
                if (match.Success)
                    return match.Value.Trim();
                break;

            case ClarificationType.AmbiguousUnit:
                // Look for unit clarifications: cucharada sopera, cucharadita, grande, etc.
                var unitKeywords = new[] { "sopera", "postre", "café", "cucharadita", "cucharada grande", "tbsp", "tsp", "tablespoon", "teaspoon" };
                foreach (var keyword in unitKeywords)
                {
                    if (responseLower.Contains(keyword))
                        return keyword;
                }
                break;
        }

        return null;
    }

    private static string BuildConversationContext(List<ConversationMessage> history)
    {
        // Build a comprehensive context from all messages, including Q&A pairs
        // This helps the LLM understand what clarifications were asked and answered
        var sb = new System.Text.StringBuilder();
        
        bool isFirst = true;
        for (int i = 0; i < history.Count; i++)
        {
            var msg = history[i];
            
            if (msg.Role == "user")
            {
                if (isFirst)
                {
                    // First user message is the meal description
                    sb.Append($"Meal description: {msg.Content}");
                    isFirst = false;
                }
                else
                {
                    // Check if previous message was assistant asking for clarification
                    if (i > 0 && history[i - 1].Role == "assistant")
                    {
                        var question = history[i - 1].Content;
                        sb.Append($"\nClarification question: {question}");
                        sb.Append($"\nUser answered: {msg.Content}");
                    }
                    else
                    {
                        sb.Append($"\nAdditional info: {msg.Content}");
                    }
                }
            }
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Gets the original meal description from conversation history.
    /// This is typically the first user message that describes the meal.
    /// </summary>
    private static string GetOriginalMealDescriptionFromHistory(List<ConversationMessage> history)
    {
        // Get the first user message, which typically contains the meal description
        var firstUserMessage = history.FirstOrDefault(m => m.Role == "user");
        if (firstUserMessage != null)
        {
            return firstUserMessage.Content;
        }
        
        // Fallback: concatenate all user messages
        return string.Join(" ", history.Where(m => m.Role == "user").Select(m => m.Content));
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

        // Save session to Firestore for persistence across restarts
        if (_memoryService != null && !string.IsNullOrEmpty(loginResponse.SessionKey))
        {
            try
            {
                await _memoryService.SaveSessionAsync(
                    chatId,
                    loginResponse.Id,
                    loginResponse.SessionKey,
                    email,
                    ct);
                _logger.LogInformation("Saved session to Firestore for user {Email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save session to Firestore for user {Email}", email);
                // Don't fail the login if session persistence fails
            }
        }

        var successReply =
            "✅ <b>Inicio de sesión exitoso.</b>\n\n" +
            "Ahora puedes registrar tus comidas usando el comando /start.\n" +
            "Usa /preferences para ver y gestionar tus preferencias guardadas.";
        await _telegramService.SendMessageAsync(chatId, successReply, "HTML", ct);
    }

    #region Memory and Preferences Handlers

    private async Task HandlePreferencesCommandAsync(string chatId, CancellationToken ct)
    {
        if (_memoryService == null)
        {
            await _telegramService.SendMessageAsync(chatId,
                "⚠️ El servicio de memoria no está disponible.", null, ct);
            return;
        }

        if (!_userSessions.TryGetValue(chatId, out var userInfo) || userInfo == null ||
            string.IsNullOrWhiteSpace(userInfo.SessionKey))
        {
            await _telegramService.SendMessageAsync(chatId,
                "⚠️ Primero debes iniciar sesión con:\n<b>/login &lt;email&gt; &lt;password&gt;</b>",
                "HTML", ct);
            return;
        }

        // Initialize conversation for preferences if needed
        if (userInfo.Conversation == null || userInfo.Conversation.IsExpired)
        {
            userInfo.Conversation = new ConversationSession
            {
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };
        }

        userInfo.Conversation.State = ConversationState.AwaitingPreferenceAction;
        userInfo.Conversation.Touch();

        var aliases = await _memoryService.GetUserAliasesAsync(chatId, ct);

        var msg = "⚙️ <b>Gestión de Preferencias</b>\n\n";

        if (aliases.Count > 0)
        {
            msg += "<b>Tus alias guardados:</b>\n";
            msg += string.Join("\n", aliases.Take(10).Select((a, i) => 
                $"{i + 1}. \"{a.InputTerm}\" → {a.ResolvedFoodName} ({a.UseCount}x)"));
            
            if (aliases.Count > 10)
                msg += $"\n... y {aliases.Count - 10} más";
            
            msg += "\n\n";
        }
        else
        {
            msg += "<i>No tienes alias guardados todavía.</i>\n\n";
        }

        msg += "<b>Opciones:</b>\n" +
               "1️⃣ <b>Crear</b> nuevo alias\n" +
               "2️⃣ <b>Eliminar</b> un alias\n" +
               "3️⃣ <b>Salir</b>\n\n" +
               "Responde con el número de la opción.";

        await _telegramService.SendMessageAsync(chatId, msg, "HTML", ct);
    }

    private async Task HandlePreferenceActionResponseAsync(string chatId, CronometerUserInfo userInfo, string text, CancellationToken ct)
    {
        userInfo.Conversation!.Touch();
        var trimmed = text.Trim().ToLowerInvariant();

        if (trimmed == "1" || trimmed.Contains("crear") || trimmed.Contains("nuevo"))
        {
            userInfo.Conversation.State = ConversationState.AwaitingAliasInput;
            await _telegramService.SendMessageAsync(chatId,
                "📝 <b>Crear nuevo alias</b>\n\n" +
                "Escribe el término que usas normalmente.\n" +
                "Ejemplo: \"pollo\", \"arroz integral\", \"mi proteina\"",
                "HTML", ct);
        }
        else if (trimmed == "2" || trimmed.Contains("eliminar") || trimmed.Contains("borrar"))
        {
            if (_memoryService == null)
            {
                await _telegramService.SendMessageAsync(chatId, "⚠️ Servicio no disponible.", null, ct);
                return;
            }

            var aliases = await _memoryService.GetUserAliasesAsync(chatId, ct);
            if (aliases.Count == 0)
            {
                await _telegramService.SendMessageAsync(chatId,
                    "No tienes alias para eliminar. Usa /preferences para volver al menú.",
                    null, ct);
                userInfo.Conversation.State = ConversationState.Idle;
                return;
            }

            userInfo.Conversation.State = ConversationState.AwaitingAliasDeleteConfirm;
            var msg = "🗑️ <b>Eliminar alias</b>\n\n" +
                      "Selecciona el número del alias a eliminar:\n\n" +
                      string.Join("\n", aliases.Take(15).Select((a, i) => 
                          $"{i + 1}. \"{a.InputTerm}\" → {a.ResolvedFoodName}"));

            await _telegramService.SendMessageAsync(chatId, msg, "HTML", ct);
        }
        else if (trimmed == "3" || trimmed.Contains("salir") || trimmed.Contains("cancelar"))
        {
            userInfo.Conversation.State = ConversationState.Idle;
            await _telegramService.SendMessageAsync(chatId,
                "👋 Saliste del menú de preferencias. Usa /start para registrar comidas.",
                null, ct);
        }
        else
        {
            await _telegramService.SendMessageAsync(chatId,
                "Por favor, responde con 1, 2 o 3.",
                null, ct);
        }
    }

    private async Task HandleAliasInputResponseAsync(string chatId, CronometerUserInfo userInfo, string text, CancellationToken ct)
    {
        userInfo.Conversation!.Touch();
        userInfo.Conversation.CurrentAliasInputTerm = text.Trim();
        userInfo.Conversation.State = ConversationState.AwaitingFoodSearch;

        await _telegramService.SendMessageAsync(chatId,
            $"Término guardado: <b>{text.Trim()}</b>\n\n" +
            "Ahora escribe el nombre del alimento a buscar en Cronometer:",
            "HTML", ct);
    }

    private async Task HandleFoodSearchResponseAsync(string chatId, CronometerUserInfo userInfo, string text, CancellationToken ct)
    {
        userInfo.Conversation!.Touch();
        await _telegramService.SendMessageAsync(chatId, "🔍 Buscando...", null, ct);

        try
        {
            var auth = new AuthPayload { UserId = userInfo.UserId!.Value, Token = userInfo.SessionKey! };
            var (_, _, _, candidates) = await _cronometerService.SearchFoodWithCandidatesAsync(text.Trim(), auth, ct);

            if (candidates.Count == 0)
            {
                await _telegramService.SendMessageAsync(chatId,
                    "❌ No encontré resultados. Intenta con otro término de búsqueda:",
                    null, ct);
                return;
            }

            userInfo.Conversation.CurrentSearchResults = candidates;
            userInfo.Conversation.State = ConversationState.AwaitingFoodSelection;

            var msg = "📋 <b>Resultados de búsqueda:</b>\n\n" +
                      string.Join("\n", candidates.Take(10).Select((c, i) => 
                          $"{i + 1}. {c.Food.Name} <i>[{c.SourceTab}]</i>")) +
                      "\n\nResponde con el número para seleccionar, o escribe otro término para buscar de nuevo.";

            await _telegramService.SendMessageAsync(chatId, msg, "HTML", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching food for alias");
            await _telegramService.SendMessageAsync(chatId,
                "❌ Error al buscar. Intenta de nuevo.",
                null, ct);
        }
    }

    private async Task HandleFoodSelectionResponseAsync(string chatId, CronometerUserInfo userInfo, string text, CancellationToken ct)
    {
        userInfo.Conversation!.Touch();

        // Check if user typed a number
        if (int.TryParse(text.Trim(), out int selection) && 
            selection >= 1 && 
            selection <= userInfo.Conversation.CurrentSearchResults.Count)
        {
            var selected = userInfo.Conversation.CurrentSearchResults[selection - 1];
            var inputTerm = userInfo.Conversation.CurrentAliasInputTerm ?? text;

            if (_memoryService != null)
            {
                await _memoryService.SaveAliasAsync(
                    chatId,
                    inputTerm,
                    selected.Food.Name,
                    selected.Food.Id,
                    selected.SourceTab,
                    isManual: true,
                    ct);

                await _telegramService.SendMessageAsync(chatId,
                    $"✅ <b>Alias guardado!</b>\n\n" +
                    $"\"{inputTerm}\" → {selected.Food.Name}\n\n" +
                    "Usa /preferences para ver todos tus alias.",
                    "HTML", ct);
            }

            // Reset state
            userInfo.Conversation.CurrentAliasInputTerm = null;
            userInfo.Conversation.CurrentSearchResults.Clear();
            userInfo.Conversation.State = ConversationState.Idle;
        }
        else
        {
            // User typed a new search term
            await HandleFoodSearchResponseAsync(chatId, userInfo, text, ct);
        }
    }

    private async Task HandleAliasDeleteConfirmResponseAsync(string chatId, CronometerUserInfo userInfo, string text, CancellationToken ct)
    {
        userInfo.Conversation!.Touch();

        if (_memoryService == null)
        {
            await _telegramService.SendMessageAsync(chatId, "⚠️ Servicio no disponible.", null, ct);
            return;
        }

        var aliases = await _memoryService.GetUserAliasesAsync(chatId, ct);

        if (int.TryParse(text.Trim(), out int selection) && selection >= 1 && selection <= aliases.Count)
        {
            var aliasToDelete = aliases[selection - 1];
            await _memoryService.DeleteAliasAsync(aliasToDelete.Id, ct);

            await _telegramService.SendMessageAsync(chatId,
                $"🗑️ Alias eliminado: \"{aliasToDelete.InputTerm}\" → {aliasToDelete.ResolvedFoodName}\n\n" +
                "Usa /preferences para volver al menú.",
                null, ct);

            userInfo.Conversation.State = ConversationState.Idle;
        }
        else
        {
            await _telegramService.SendMessageAsync(chatId,
                "Por favor, responde con un número válido o /cancel para salir.",
                null, ct);
        }
    }

    private async Task HandleMemoryConfirmationResponseAsync(string chatId, CronometerUserInfo userInfo, string text, CancellationToken ct)
    {
        userInfo.Conversation!.Touch();
        var trimmed = text.Trim().ToLowerInvariant();
        var pendingLearnings = userInfo.Conversation.PendingLearnings;

        if (_memoryService == null || pendingLearnings.Count == 0)
        {
            userInfo.Conversation = null;
            await _telegramService.SendMessageAsync(chatId,
                "✅ Listo. Usa /start para registrar otra comida.",
                null, ct);
            return;
        }

        List<PendingLearning> learningsToSave = new();

        if (trimmed == "si" || trimmed == "sí" || trimmed == "yes" || trimmed == "s")
        {
            // Save all learnings
            learningsToSave = pendingLearnings;
        }
        else if (trimmed == "no" || trimmed == "n")
        {
            // Don't save any
            userInfo.Conversation = null;
            await _telegramService.SendMessageAsync(chatId,
                "👍 Entendido. No se guardaron preferencias.\nUsa /start para registrar otra comida.",
                null, ct);
            return;
        }
        else
        {
            // Try to parse specific numbers (e.g., "1,3" or "1 3")
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
                await _telegramService.SendMessageAsync(chatId,
                    "Por favor, responde 'si', 'no', o los números de las preferencias a guardar (ej: 1,3).",
                    null, ct);
                return;
            }
        }

        // Save the selected learnings
        foreach (var learning in learningsToSave)
        {
            try
            {
                await _memoryService.SaveAliasAsync(
                    chatId,
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

        userInfo.Conversation = null;
        await _telegramService.SendMessageAsync(chatId,
            $"🧠 <b>¡{learningsToSave.Count} preferencia(s) guardada(s)!</b>\n\n" +
            "La próxima vez que uses estos términos, los reconoceré automáticamente.\n" +
            "Usa /start para registrar otra comida o /preferences para ver tus preferencias.",
            "HTML", ct);
    }

    private async Task HandleSearchCommandAsync(string chatId, string text, CancellationToken ct)
    {
        if (!_userSessions.TryGetValue(chatId, out var userInfo) || userInfo == null ||
            string.IsNullOrWhiteSpace(userInfo.SessionKey))
        {
            await _telegramService.SendMessageAsync(chatId,
                "⚠️ Primero debes iniciar sesión con:\n<b>/login &lt;email&gt; &lt;password&gt;</b>",
                "HTML", ct);
            return;
        }

        // Extract search query from command
        var query = text.Replace("/search", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("/buscar", "", StringComparison.OrdinalIgnoreCase)
                       .Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            await _telegramService.SendMessageAsync(chatId,
                "Uso: /search [nombre del alimento]\nEjemplo: /search chicken breast",
                null, ct);
            return;
        }

        await _telegramService.SendMessageAsync(chatId, "🔍 Buscando...", null, ct);

        try
        {
            var auth = new AuthPayload { UserId = userInfo.UserId!.Value, Token = userInfo.SessionKey! };
            var (_, _, _, candidates) = await _cronometerService.SearchFoodWithCandidatesAsync(query, auth, ct);

            if (candidates.Count == 0)
            {
                await _telegramService.SendMessageAsync(chatId,
                    $"❌ No encontré resultados para \"{query}\".",
                    null, ct);
                return;
            }

            var msg = $"📋 <b>Resultados para \"{query}\":</b>\n\n" +
                      string.Join("\n", candidates.Take(10).Select((c, i) => 
                          $"{i + 1}. {c.Food.Name} <i>[{c.SourceTab}]</i> (Score: {c.Score:F2})"));

            await _telegramService.SendMessageAsync(chatId, msg, "HTML", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in search command");
            await _telegramService.SendMessageAsync(chatId,
                "❌ Error al buscar. Intenta de nuevo.",
                null, ct);
        }
    }

    private async Task HandleAlternativeSearchAsync(string chatId, CronometerUserInfo userInfo, int itemIndex, CancellationToken ct)
    {
        var item = userInfo.Conversation!.ValidatedFoods[itemIndex];
        
        await _telegramService.SendMessageAsync(chatId, 
            $"🔍 Buscando alternativas para: <b>{item.OriginalName}</b>...", 
            "HTML", ct);

        try
        {
            var auth = new AuthPayload { UserId = userInfo.UserId!.Value, Token = userInfo.SessionKey! };
            var (_, _, _, candidates) = await _cronometerService.SearchFoodWithCandidatesAsync(item.OriginalName, auth, ct);

            if (candidates.Count <= 1)
            {
                await _telegramService.SendMessageAsync(chatId,
                    "No hay alternativas disponibles. Intenta escribir un nombre diferente.",
                    null, ct);
                return;
            }

            // Store context for selection
            userInfo.Conversation.CurrentSearchResults = candidates;
            userInfo.Conversation.CurrentSearchItemIndex = itemIndex;
            userInfo.Conversation.State = ConversationState.AwaitingFoodSearchSelection;

            var msg = $"📋 <b>Alternativas para \"{item.OriginalName}\":</b>\n" +
                      $"(Actualmente: {item.FoodName})\n\n" +
                      string.Join("\n", candidates.Take(10).Select((c, i) => 
                      {
                          var current = c.Food.Id == item.FoodId ? " ✓" : "";
                          return $"{i + 1}. {c.Food.Name} <i>[{c.SourceTab}]</i>{current}";
                      })) +
                      "\n\nResponde con el número para seleccionar, o /cancel para mantener el actual.";

            await _telegramService.SendMessageAsync(chatId, msg, "HTML", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching alternatives");
            await _telegramService.SendMessageAsync(chatId,
                "❌ Error al buscar alternativas. Intenta de nuevo.",
                null, ct);
        }
    }

    private async Task HandleFoodSearchSelectionResponseAsync(string chatId, CronometerUserInfo userInfo, string text, CancellationToken ct)
    {
        userInfo.Conversation!.Touch();

        if (!int.TryParse(text.Trim(), out int selection) || 
            selection < 1 || 
            selection > userInfo.Conversation.CurrentSearchResults.Count)
        {
            await _telegramService.SendMessageAsync(chatId,
                "Por favor, responde con un número válido o /cancel.",
                null, ct);
            return;
        }

        var selected = userInfo.Conversation.CurrentSearchResults[selection - 1];
        var itemIndex = userInfo.Conversation.CurrentSearchItemIndex ?? 0;

        // Get full food info
        var auth = new AuthPayload { UserId = userInfo.UserId!.Value, Token = userInfo.SessionKey! };
        var food = (await _cronometerClient.GetFoodsAsync(new()
        {
            Ids = [selected.Food.Id],
            Auth = auth,
        }, ct)).Foods?.FirstOrDefault();

        if (food == null)
        {
            await _telegramService.SendMessageAsync(chatId,
                "❌ Error al obtener información del alimento.",
                null, ct);
            return;
        }

        // Update the validated item
        var item = userInfo.Conversation.ValidatedFoods[itemIndex];
        var originalName = item.OriginalName;
        var (measure, isRawGrams) = CronometerService.GetSimilarMeasureIdStatic(food.Measures, item.MeasureName);

        // Update with new food
        item.FoodName = food.Name;
        item.FoodId = food.Id;
        item.MeasureId = measure.Id;
        item.MeasureGrams = measure.Value;
        item.MeasureName = isRawGrams ? "g" : measure.Name;
        item.IsRawGrams = isRawGrams;
        item.SourceTab = selected.SourceTab;
        item.WasResolvedFromAlias = false;
        item.AliasId = null;

        // Add to pending learnings (since user explicitly chose this)
        if (!userInfo.Conversation.PendingLearnings.Any(p => 
            p.OriginalTerm.Equals(originalName, StringComparison.OrdinalIgnoreCase)))
        {
            userInfo.Conversation.PendingLearnings.Add(new PendingLearning
            {
                OriginalTerm = originalName,
                ResolvedFoodName = food.Name,
                ResolvedFoodId = food.Id,
                SourceTab = selected.SourceTab,
                IsFoodAlias = true
            });
        }

        // Clear search context
        userInfo.Conversation.CurrentSearchResults.Clear();
        userInfo.Conversation.CurrentSearchItemIndex = null;
        userInfo.Conversation.State = ConversationState.AwaitingConfirmation;

        // Show updated list
        var itemsSummary = string.Join("\n", userInfo.Conversation.ValidatedFoods.Select((i, idx) =>
        {
            var aliasIndicator = i.WasResolvedFromAlias ? " 🧠" : "";
            var changedIndicator = idx == itemIndex ? " ✏️" : "";
            return $"{idx + 1}. {i.DisplayQuantity} de <b>{i.FoodName}</b>{aliasIndicator}{changedIndicator}";
        }));

        var msg = $"✅ <b>Actualizado!</b>\n\n" +
                  $"<b>Alimentos:</b>\n{itemsSummary}\n\n" +
                  "✏️ = modificado\n" +
                  "🧠 = desde tu memoria\n\n" +
                  "Usa <b>/save</b> para guardar o responde con un número para más cambios.";

        await _telegramService.SendMessageAsync(chatId, msg, "HTML", ct);
    }

    #endregion

    /// <summary>
    /// Enriches clarification items with the original food terms from user input.
    /// This maps LLM-translated names (e.g., "Egg") back to original terms (e.g., "huevos").
    /// </summary>
    private static void EnrichClarificationsWithOriginalTerms(List<ClarificationItem> clarifications, string originalInput)
    {
        if (string.IsNullOrWhiteSpace(originalInput))
            return;

        var inputLower = originalInput.ToLowerInvariant();
        
        // Common food terms in Spanish and their potential English translations
        var commonTranslations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "huevo", new[] { "egg", "eggs" } },
            { "huevos", new[] { "egg", "eggs" } },
            { "pollo", new[] { "chicken" } },
            { "arroz", new[] { "rice" } },
            { "leche", new[] { "milk" } },
            { "pan", new[] { "bread" } },
            { "carne", new[] { "meat", "beef" } },
            { "pescado", new[] { "fish" } },
            { "queso", new[] { "cheese" } },
            { "mantequilla", new[] { "butter" } },
            { "aceite", new[] { "oil" } },
            { "azucar", new[] { "sugar" } },
            { "azúcar", new[] { "sugar" } },
            { "sal", new[] { "salt" } },
            { "agua", new[] { "water" } },
            { "café", new[] { "coffee" } },
            { "cafe", new[] { "coffee" } },
            { "té", new[] { "tea" } },
            { "te", new[] { "tea" } },
            { "manzana", new[] { "apple" } },
            { "banana", new[] { "banana" } },
            { "platano", new[] { "banana", "plantain" } },
            { "plátano", new[] { "banana", "plantain" } },
            { "naranja", new[] { "orange" } },
            { "tomate", new[] { "tomato" } },
            { "papa", new[] { "potato" } },
            { "patata", new[] { "potato" } },
            { "zanahoria", new[] { "carrot" } },
            { "cebolla", new[] { "onion" } },
            { "ajo", new[] { "garlic" } },
            { "proteina", new[] { "protein" } },
            { "proteína", new[] { "protein" } },
            { "avena", new[] { "oatmeal", "oats" } },
            { "yogur", new[] { "yogurt" } },
            { "yogurt", new[] { "yogurt" } },
        };

        foreach (var clarification in clarifications)
        {
            var itemNameLower = clarification.ItemName.ToLowerInvariant();
            
            // Strategy 1: Direct search in original input
            foreach (var (spanish, englishVariants) in commonTranslations)
            {
                if (englishVariants.Any(en => itemNameLower.Contains(en)) && 
                    inputLower.Contains(spanish.ToLowerInvariant()))
                {
                    clarification.OriginalTerm = spanish;
                    break;
                }
            }

            // Strategy 2: If no translation found, try to find any word from itemName in input
            if (string.IsNullOrEmpty(clarification.OriginalTerm))
            {
                var inputWords = originalInput.Split(new[] { ' ', ',', '.', '!', '?', '\n', '\r' }, 
                    StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var word in inputWords)
                {
                    // Skip common words and numbers
                    if (word.Length < 3 || double.TryParse(word, out _))
                        continue;
                    
                    // Check if this word might be the food term
                    var wordLower = word.ToLowerInvariant();
                    if (commonTranslations.ContainsKey(wordLower) || 
                        itemNameLower.Contains(wordLower) ||
                        wordLower.Contains(itemNameLower.Split(' ').FirstOrDefault() ?? ""))
                    {
                        clarification.OriginalTerm = word;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Replaces detected aliases in the user's text with their resolved food names.
    /// This helps the LLM understand what the user means without asking clarifying questions.
    /// </summary>
    private static string ReplaceAliasesInText(string text, List<DetectedAlias> detectedAliases)
    {
        if (detectedAliases.Count == 0)
            return text;

        // Sort by start index descending so we replace from end to start
        // This preserves the indices of earlier matches
        var sortedAliases = detectedAliases
            .OrderByDescending(a => a.StartIndex)
            .ToList();

        var result = text;
        foreach (var detected in sortedAliases)
        {
            // Replace the matched term with the resolved food name
            var before = result.Substring(0, detected.StartIndex);
            var after = result.Substring(detected.StartIndex + detected.Length);
            result = before + detected.Alias.ResolvedFoodName + after;
        }

        return result;
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
