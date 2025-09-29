using System.Collections.Concurrent;
using System.Text.Json;
using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.CronometerClient.Requests;
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
    private readonly GeminiHttpClient _geminiClient;
    private readonly CronometerService _cronometerService;

    public CronometerPollingHostedService(ILogger<CronometerPollingHostedService> logger, TelegramService service, CronometerHttpClient cronometerClient, GeminiHttpClient geminiClient, CronometerService cronometerService)
    {
        _logger = logger;
        _telegramService = service;
        _cronometerClient = cronometerClient;
        _geminiClient = geminiClient;
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
            _logger.LogInformation("[Telegram] {ChatId}: {Text}", chatId.Value, text);

            if (IsLoginMessage(text))
            {
                await HandleLoginAsync(chatId.Value.ToString(), text, ct);
            }
            else
            {
                await HandleLogMessageAsync(chatId.Value.ToString(), text, ct);
            }
        }
    }

    private static bool IsLoginMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.Trim().ToLowerInvariant();
        return lower.Contains("login") || lower.Contains("log in") || lower.Contains("sign in");
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
        await _telegramService.SendMessageAsync(chatId, "Iniciando sesión...", null, ct);

        // var loginResponse = await _cronometerClient.LoginAsync(new(email, password), ct);
        var loginResponse = _cronometerClient.LoginMock();
        if (loginResponse.Result == "FAIL")
        {
            var reply = "Error de autenticación. Por favor, verifique sus credenciales.";
            await _telegramService.SendMessageAsync(chatId, reply, null, ct);
            return;
        }

        var userInfo = new CronometerUserInfo
        {
            Email = email,
            Password = password,
            UserId = loginResponse.UserId,
            SessionKey = loginResponse.SessionKey,
        };

        _userSessions.AddOrUpdate(chatId, userInfo, (key, oldValue) => userInfo);

        var successReply =
            "Inicio de sesión exitoso. Ahora puedes registrar tus comidas enviando mensajes de texto.\n" +
            "Recuerda, puedes indicar los siguientes datos para cada comida:\n\n" +
            "<b>✅ Hora de la comida.</b>\n\n" +
            "<b>✅ Tipo de comida</b>: Desayuno, Almuerzo, Cena, Merienda.\n\n" +
            "<b>✅ Pesos por cada comida o tamaño de la porción</b>: Por ejemplo, <i>70 gr de zanahoria, " +
            "2 HUEVOS PEQUEÑOS, 2 tortillas de harina de trigo, 2 cucharadas de aceite de oliva, etc.</i>";
        await _telegramService.SendMessageAsync(chatId, successReply, "HTML", ct);
    }

    private async Task HandleLogMessageAsync(string chatId, string text, CancellationToken ct)
    {
        // Check if user is logged in
        if (!_userSessions.TryGetValue(chatId, out var userInfo) ||
            userInfo == null ||
            string.IsNullOrWhiteSpace(userInfo.SessionKey) ||
            userInfo.UserId == null)
        {
            var reply = "No estás autenticado. Por favor, inicia sesión usando el comando:\n" +
                        "<b>/login &lt;email&gt; &lt;password&gt;</b>";
            await _telegramService.SendMessageAsync(chatId, reply, "HTML", ct);
            return;
        }

        var replyProcessing = "Procesando tu mensaje...";
        await _telegramService.SendMessageAsync(chatId, replyProcessing, null, ct);

        var prompt = await File.ReadAllTextAsync("Clients/GeminiClient/CronometerPrompt.txt", ct);
        prompt = prompt.Replace("@Now", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
        prompt = prompt.Replace("@UserInput", text);

        try
        {
            var geminiResponse = await _geminiClient.GenerateTextAsync(prompt, ct);
            var foodInfo = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault();

            if (foodInfo == null ||
                string.IsNullOrWhiteSpace(foodInfo.Text) ||
                foodInfo.Text.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                var errorReply = "No se pudo procesar tu mensaje. Por favor, intenta nuevamente.";
                await _telegramService.SendMessageAsync(chatId, errorReply, null, ct);
                return;
            }

            var logMealRequest = JsonSerializer.Deserialize<LogMealRequest>(RemoveMarkdown(foodInfo.Text), new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            });
            var logSuccess = await _cronometerService.LogMealAsync(new AuthPayload
            {
                UserId = userInfo.UserId.Value,
                Token = userInfo.SessionKey
            }, logMealRequest!, ct);

            if (logSuccess)
            {
                var successReply = "Comida registrada exitosamente.";
                await _telegramService.SendMessageAsync(chatId, successReply, null, ct);
            }
            else
            {
                var errorReply = "No se pudo registrar la comida. Por favor, intenta nuevamente.";
                await _telegramService.SendMessageAsync(chatId, errorReply, null, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating content with Gemini for chatId {ChatId}", chatId);
            var errorReply = "Algo falló, revisa el contenido del mensaje o logueate de nuevo. Luego, vuelve a intentar.";
            await _telegramService.SendMessageAsync(chatId, errorReply, null, ct);
            return;
        }
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
        return noCodeBlock;
    }
}
