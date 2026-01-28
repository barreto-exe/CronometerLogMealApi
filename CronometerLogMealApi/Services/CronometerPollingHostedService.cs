using System.Collections.Concurrent;
using System.Text.Json;
using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Clients.OpenAIClient;
using CronometerLogMealApi.Clients.GeminiClient;
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
    private readonly CronometerHttpClient _cronometerClient;
    private readonly OpenAIHttpClient _openAIClient;
    private readonly CronometerService _cronometerService;

    public CronometerPollingHostedService(ILogger<CronometerPollingHostedService> logger, TelegramService service, CronometerHttpClient cronometerClient, OpenAIHttpClient openAIClient, CronometerService cronometerService)
    {
        _logger = logger;
        _telegramService = service;
        _cronometerClient = cronometerClient;
        _openAIClient = openAIClient;
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
        var text = msg?.Text;
        var chatId = msg?.Chat?.Id;
        
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
