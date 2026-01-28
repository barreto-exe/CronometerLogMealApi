using System.Collections.Concurrent;
using System.Text.Json;
using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Clients.OpenAIClient;
using CronometerLogMealApi.Clients.GeminiClient;
using CronometerLogMealApi.Clients.TelegramClient;
using CronometerLogMealApi.Clients.TelegramClient.Models;
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
    private readonly GeminiHttpClient _geminiClient;
    private readonly TesseractOcrService _ocrService;
    private readonly CronometerService _cronometerService;

    public CronometerPollingHostedService(
        ILogger<CronometerPollingHostedService> logger, 
        TelegramService service, 
        TelegramHttpClient telegramClient,
        CronometerHttpClient cronometerClient, 
        OpenAIHttpClient openAIClient, 
        GeminiHttpClient geminiClient,
        TesseractOcrService ocrService,
        CronometerService cronometerService)
    {
        _logger = logger;
        _telegramService = service;
        _telegramClient = telegramClient;
        _cronometerClient = cronometerClient;
        _openAIClient = openAIClient;
        _geminiClient = geminiClient;
        _ocrService = ocrService;
        _cronometerService = cronometerService;
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
        var chatId = msg?.Chat?.Id;
        
        if (!chatId.HasValue) return;
        
        var chatIdStr = chatId.Value.ToString();

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

        // Check for photos first
        if (msg?.Photo != null && msg.Photo.Count > 0)
        {
            _logger.LogInformation("[Telegram] {ChatId}: Received photo with {Count} sizes", chatIdStr, msg.Photo.Count);
            await HandlePhotoMessageAsync(chatIdStr, msg, ct);
            return;
        }

        var text = msg?.Text;
        
        if (!string.IsNullOrWhiteSpace(text))
        {
            _logger.LogInformation("[Telegram] {ChatId}: {Text}", chatIdStr, text);

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

    private async Task HandlePhotoMessageAsync(string chatId, TelegramMessage msg, CancellationToken ct)
    {
        // Check if user is authenticated
        if (!_userSessions.TryGetValue(chatId, out var userInfo) || userInfo == null ||
            string.IsNullOrWhiteSpace(userInfo.SessionKey))
        {
            await _telegramService.SendMessageAsync(chatId,
                "⚠️ Primero debes iniciar sesión con:\n<b>/login &lt;email&gt; &lt;password&gt;</b>",
                "HTML", ct);
            return;
        }

        // Auto-start a session if none exists
        if (userInfo.Conversation == null || userInfo.Conversation.State == ConversationState.Idle)
        {
            userInfo.Conversation = new ConversationSession
            {
                State = ConversationState.Processing,
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                MessageHistory = new List<ConversationMessage>()
            };
        }

        userInfo.Conversation.Touch();
        await _telegramService.SendMessageAsync(chatId, "📸 Procesando imagen...", null, ct);

        try
        {
            // Get the largest photo (best quality)
            var photo = msg.Photo!.OrderByDescending(p => p.FileSize ?? 0).First();
            
            // Download the photo
            var fileInfo = await _telegramClient.GetFileAsync(photo.FileId, ct);
            if (fileInfo == null || string.IsNullOrEmpty(fileInfo.FilePath))
            {
                await _telegramService.SendMessageAsync(chatId,
                    "❌ No se pudo obtener la imagen. Por favor, intenta enviarla nuevamente.",
                    null, ct);
                userInfo.Conversation.State = ConversationState.AwaitingMealDescription;
                return;
            }

            var imageBytes = await _telegramClient.DownloadFileAsync(fileInfo.FilePath, ct);
            _logger.LogInformation("Downloaded image: {Size} bytes", imageBytes.Length);

            // Process the image with Vision API
            var ocrResult = await ProcessImageAsync(imageBytes, msg.Caption, ct);

            if (!string.IsNullOrEmpty(ocrResult.Error))
            {
                await _telegramService.SendMessageAsync(chatId,
                    $"❌ {ocrResult.Error}",
                    null, ct);
                userInfo.Conversation.State = ConversationState.AwaitingMealDescription;
                return;
            }

            // Store OCR result in conversation
            userInfo.Conversation.OriginalDescription = ocrResult.Transcription;
            userInfo.Conversation.MessageHistory.Add(new ConversationMessage
            {
                Role = "user",
                Content = $"[Imagen transcrita]: {ocrResult.Transcription}",
                Timestamp = DateTime.UtcNow
            });

            // Check if clarification is needed
            if (ocrResult.NeedsClarification && ocrResult.ClarificationQuestions.Count > 0)
            {
                userInfo.Conversation.State = ConversationState.AwaitingClarification;

                // Store OCR data for later use
                userInfo.Conversation.PendingMealRequest = null; // Will be set after clarification

                var transcriptionPreview = ocrResult.Transcription.Length > 200 
                    ? ocrResult.Transcription.Substring(0, 200) + "..." 
                    : ocrResult.Transcription;

                var message = $"📝 <b>Texto detectado:</b>\n<i>{transcriptionPreview}</i>\n\n";
                
                // Add info about what was found
                if (ocrResult.People.Count > 0)
                {
                    message += $"👥 <b>Personas:</b> {string.Join(", ", ocrResult.People)}\n";
                }
                if (ocrResult.MealTypes.Count > 0)
                {
                    message += $"🍽️ <b>Comidas:</b> {string.Join(", ", ocrResult.MealTypes)}\n";
                }
                if (!string.IsNullOrEmpty(ocrResult.Date))
                {
                    message += $"📅 <b>Fecha:</b> {ocrResult.Date}\n";
                }

                message += "\n🤔 <b>Necesito clarificación:</b>\n";
                message += string.Join("\n", ocrResult.ClarificationQuestions.Select((q, i) => $"{i + 1}. {q}"));

                // Add uncertain items if any
                if (ocrResult.UncertainItems.Count > 0)
                {
                    message += "\n\n⚠️ <b>Items con lectura incierta:</b>\n";
                    foreach (var item in ocrResult.UncertainItems)
                    {
                        if (!string.IsNullOrEmpty(item.Question))
                        {
                            message += $"• {item.Question}\n";
                        }
                    }
                }

                userInfo.Conversation.MessageHistory.Add(new ConversationMessage
                {
                    Role = "assistant",
                    Content = message,
                    Timestamp = DateTime.UtcNow
                });

                await _telegramService.SendMessageAsync(chatId, message, "HTML", ct);
                return;
            }

            // If no clarification needed but we have multiple sections, ask which one
            if (ocrResult.Sections.Count > 1)
            {
                userInfo.Conversation.State = ConversationState.AwaitingClarification;
                
                var optionsMessage = "📋 <b>Encontré múltiples registros:</b>\n\n";
                for (int i = 0; i < ocrResult.Sections.Count; i++)
                {
                    var section = ocrResult.Sections[i];
                    var itemCount = section.Items.Count;
                    optionsMessage += $"{i + 1}. <b>{section.Person}</b> - {section.MealType} ({itemCount} items)\n";
                }
                optionsMessage += "\n¿Cuál deseas registrar? (responde con el número o \"todos\")";

                await _telegramService.SendMessageAsync(chatId, optionsMessage, "HTML", ct);
                return;
            }

            // Single section, process directly
            if (ocrResult.Sections.Count == 1)
            {
                var section = ocrResult.Sections[0];
                var mealDescription = BuildMealDescriptionFromSection(section, ocrResult.Date);
                
                // Process as a regular meal description
                await ProcessAndLogMealFromImageAsync(chatId, userInfo, mealDescription, section, ocrResult.Date, ct);
            }
            else
            {
                await _telegramService.SendMessageAsync(chatId,
                    "❌ No pude extraer información de comida de la imagen. Por favor, intenta con otra imagen o describe tu comida con texto.",
                    null, ct);
                userInfo.Conversation.State = ConversationState.AwaitingMealDescription;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing photo for chatId {ChatId}", chatId);
            await _telegramService.SendMessageAsync(chatId,
                "❌ Ocurrió un error al procesar la imagen. Por favor, intenta nuevamente.",
                null, ct);
            userInfo.Conversation.State = ConversationState.AwaitingMealDescription;
        }
    }

    private async Task<ImageOcrResult> ProcessImageAsync(byte[] imageBytes, string? caption, CancellationToken ct)
    {
        var venezuelaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Venezuela Standard Time");
        var venezuelaNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, venezuelaTimeZone);

        // Step 1: Extract text using Windows OCR (local processing)
        _logger.LogInformation("Running Windows OCR on image...");
        var rawOcrText = await _ocrService.ExtractTextAsync(imageBytes, "es");
        
        if (string.IsNullOrWhiteSpace(rawOcrText))
        {
            _logger.LogWarning("Windows OCR extracted no text from image");
            return new ImageOcrResult { Error = "No se pudo extraer texto de la imagen. La imagen puede estar vacía o el texto no es legible." };
        }
        
        _logger.LogInformation("OCR extracted text: {Text}", rawOcrText);

        // Step 2: Use LLM to interpret and structure the OCR text
        var prompt = GeminiPrompts.OcrTextInterpretationPrompt
            .Replace("@OcrText", rawOcrText)
            .Replace("@UserInstructions", caption ?? "Ninguna")
            .Replace("@Now", venezuelaNow.ToString("yyyy-MM-ddTHH:mm:ss"));

        // Use Gemini for text interpretation (not vision - uses less quota)
        var response = await _geminiClient.GenerateTextAsync(prompt, ct);
        var content = response?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        if (string.IsNullOrEmpty(content))
        {
            // If Gemini fails, return raw OCR as transcription
            _logger.LogWarning("Gemini interpretation failed, returning raw OCR text");
            return new ImageOcrResult
            {
                Transcription = rawOcrText,
                NeedsClarification = true,
                ClarificationQuestions = new List<string>
                {
                    "No pude interpretar el texto automáticamente. Por favor describe qué comida deseas registrar de esta transcripción."
                }
            };
        }

        _logger.LogInformation("LLM interpretation: {Response}", content);

        var cleanedJson = RemoveMarkdown(content);

        try
        {
            var result = JsonSerializer.Deserialize<ImageOcrResult>(cleanedJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return result ?? new ImageOcrResult { Error = "No se pudo interpretar la respuesta." };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM response: {Response}", cleanedJson);
            // Fallback: return raw OCR text
            return new ImageOcrResult
            {
                Transcription = rawOcrText,
                NeedsClarification = true,
                ClarificationQuestions = new List<string>
                {
                    "Hubo un error procesando la imagen. Aquí está el texto detectado. ¿Qué deseas registrar?"
                }
            };
        }
    }

    private static string BuildMealDescriptionFromSection(ImageMealSection section, string? date)
    {
        var items = section.Items
            .Where(i => i.Quantity.HasValue && !string.IsNullOrEmpty(i.Name))
            .Select(i => $"{i.Quantity}{i.Unit ?? "g"} de {i.Name}")
            .ToList();

        var mealType = section.MealType.ToLower() switch
        {
            "desayuno" => "desayuno",
            "almuerzo" => "almuerzo", 
            "cena" => "cena",
            "merienda" => "merienda",
            _ => section.MealType.ToLower()
        };

        var description = $"Para el {mealType}";
        if (!string.IsNullOrEmpty(date))
        {
            description += $" del {date}";
        }
        description += ": " + string.Join(", ", items);

        return description;
    }

    private async Task ProcessAndLogMealFromImageAsync(
        string chatId,
        CronometerUserInfo userInfo,
        string mealDescription,
        ImageMealSection section,
        string? dateStr,
        CancellationToken ct)
    {
        // Parse date
        DateTime date = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
        {
            date = parsedDate;
        }

        // Build meal request from section
        var category = section.MealType.ToUpper() switch
        {
            "DESAYUNO" => "BREAKFAST",
            "ALMUERZO" => "LUNCH",
            "CENA" => "DINNER",
            "MERIENDA" => "SNACKS",
            _ => "UNCATEGORIZED"
        };

        var items = section.Items
            .Where(i => i.Quantity.HasValue && !string.IsNullOrEmpty(i.Name))
            .Select(i => new MealItem
            {
                Quantity = (float)(i.Quantity ?? 0),
                Unit = NormalizeUnit(i.Unit),
                Name = i.Name ?? ""
            })
            .ToList();

        if (items.Count == 0)
        {
            await _telegramService.SendMessageAsync(chatId,
                "❌ No se encontraron items de comida válidos en la imagen.",
                null, ct);
            userInfo.Conversation!.State = ConversationState.AwaitingMealDescription;
            return;
        }

        var mealRequest = new LogMealRequest
        {
            Category = category,
            Date = date,
            Items = items,
            LogTime = date.Date == DateTime.UtcNow.Date
        };

        // Log the meal
        await AttemptMealLoggingAsync(chatId, userInfo, mealRequest, ct);
    }

    private static string NormalizeUnit(string? unit)
    {
        if (string.IsNullOrEmpty(unit)) return "grams";
        
        return unit.ToLower() switch
        {
            "g" or "gr" or "gramos" => "grams",
            "cucharada" or "cucharadas" or "cda" => "tbsp",
            "cucharadita" or "cucharaditas" or "cdta" => "tsp",
            "taza" or "tazas" => "cup",
            "ml" or "mililitros" => "ml",
            "unidad" or "unidades" or "u" => "unit",
            "pequeño" or "pequeña" => "small",
            "mediano" or "mediana" => "medium",
            "grande" => "large",
            _ => unit
        };
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

    private async Task AttemptMealLoggingAsync(string chatId, CronometerUserInfo userInfo, LogMealRequest request, CancellationToken ct)
    {
        await _telegramService.SendMessageAsync(chatId, "📝 Registrando en Cronometer...", null, ct);

        var logResult = await _cronometerService.LogMealAsync(
            new AuthPayload { UserId = userInfo.UserId!.Value, Token = userInfo.SessionKey! },
            request, ct);

        if (logResult.Success)
        {
            // Reset session on success
            userInfo.Conversation = null;

            var itemsSummary = string.Join("\n", request.Items.Select(i => 
                $"• {i.Quantity} {i.Unit} de {i.Name}"));

            await _telegramService.SendMessageAsync(chatId,
                "✅ <b>¡Comida registrada exitosamente!</b>\n\n" +
                $"📋 <b>Categoría:</b> {request.Category}\n" +
                $"📅 <b>Fecha:</b> {request.Date:dd/MM/yyyy HH:mm}\n\n" +
                $"<b>Alimentos:</b>\n{itemsSummary}\n\n" +
                "Usa /start para registrar otra comida.",
                "HTML", ct);
        }
        else if (logResult.NotFoundItems.Count > 0)
        {
            // Items not found, ask for alternatives
            userInfo.Conversation!.State = ConversationState.AwaitingClarification;

            var notFoundList = string.Join("\n", logResult.NotFoundItems.Select(i => $"• <b>{i}</b>"));

            // Create clarification items for not-found foods
            userInfo.Conversation.PendingClarifications = logResult.NotFoundItems
                .Select(item => new ClarificationItem
                {
                    Type = ClarificationType.FoodNotFound,
                    ItemName = item,
                    Question = $"¿Podrías darme un nombre alternativo para \"{item}\"?"
                })
                .ToList();

            userInfo.Conversation.MessageHistory.Add(new ConversationMessage
            {
                Role = "assistant",
                Content = $"Estos alimentos no fueron encontrados: {string.Join(", ", logResult.NotFoundItems)}",
                Timestamp = DateTime.UtcNow
            });

            await _telegramService.SendMessageAsync(chatId,
                "⚠️ <b>Algunos alimentos no fueron encontrados en Cronometer:</b>\n\n" +
                notFoundList + "\n\n" +
                "Por favor, proporciona nombres alternativos o más específicos para estos alimentos.\n" +
                "Por ejemplo: \"pollo\" → \"pechuga de pollo\", \"carne\" → \"carne de res molida\"",
                "HTML", ct);
        }
        else
        {
            // General error
            userInfo.Conversation!.State = ConversationState.AwaitingMealDescription;
            await _telegramService.SendMessageAsync(chatId,
                $"❌ {logResult.ErrorMessage ?? "Error desconocido al registrar la comida."}\n\n" +
                "Usa /cancel para cancelar o intenta describir tu comida nuevamente.",
                null, ct);
        }
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
        var contentObj = openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content;
        var foodInfo = contentObj is string s ? s : contentObj?.ToString();

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
            "Ahora puedes registrar tus comidas usando el comando /start\n\n" +
            "Recuerda, puedes indicar los siguientes datos para cada comida:\n\n" +
            "• <b>Hora de la comida</b>\n" +
            "• <b>Tipo de comida</b>: Desayuno, Almuerzo, Cena, Merienda\n" +
            "• <b>Pesos y tamaños</b>: Por ejemplo, <i>70g de zanahoria, 2 huevos pequeños, 2 cucharadas grandes de aceite</i>";
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
