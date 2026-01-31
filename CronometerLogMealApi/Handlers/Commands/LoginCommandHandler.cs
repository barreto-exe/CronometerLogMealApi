using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Constants;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Handlers.Commands;

/// <summary>
/// Handles the /login command for user authentication.
/// </summary>
public class LoginCommandHandler : ICommandHandler
{
    private readonly ITelegramService _telegramService;
    private readonly CronometerHttpClient _cronometerClient;
    private readonly IUserMemoryService? _memoryService;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        ITelegramService telegramService,
        CronometerHttpClient cronometerClient,
        ISessionManager sessionManager,
        ILogger<LoginCommandHandler> logger,
        IUserMemoryService? memoryService = null)
    {
        _telegramService = telegramService;
        _cronometerClient = cronometerClient;
        _sessionManager = sessionManager;
        _logger = logger;
        _memoryService = memoryService;
    }

    public bool CanHandle(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var lower = command.Trim().ToLowerInvariant();
        return lower.Contains("login") || lower.Contains("log in") || lower.Contains("sign in");
    }

    public async Task HandleAsync(CommandContext context, CancellationToken ct)
    {
        var text = context.Text;
        
        // Validate format: /login <email> <password>
        if (!text.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            text.Split(' ').Length < 3)
        {
            await _telegramService.SendMessageAsync(context.ChatId, 
                "Formato de logueo inválido. Use: /login <email> <password>", null, ct);
            return;
        }

        // Extract email and password
        var parts = text.Split(' ');
        var email = parts.LastOrDefault(t => t.Contains('@'));
        var password = parts.Length > 2 ? parts[2].Trim() : null;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            await _telegramService.SendMessageAsync(context.ChatId, 
                TelegramMessages.Auth.InvalidLoginFormat, "HTML", ct);
            return;
        }

        await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Auth.LoggingIn, null, ct);

        try
        {
            var loginResponse = await _cronometerClient.LoginAsync(new(email, password), ct);
            
            if (loginResponse.Result == "FAIL")
            {
                await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Auth.LoginFailed, null, ct);
                return;
            }

            var userInfo = new Models.CronometerUserInfo
            {
                Email = email,
                Password = password,
                UserId = loginResponse.Id,
                SessionKey = loginResponse.SessionKey,
            };

            _sessionManager.SetSession(context.ChatId, userInfo);

            // Persist session if memory service is available
            if (_memoryService != null && !string.IsNullOrEmpty(loginResponse.SessionKey))
            {
                try
                {
                    await _memoryService.SaveSessionAsync(
                        context.ChatId,
                        loginResponse.Id,
                        loginResponse.SessionKey,
                        email,
                        ct);
                    _logger.LogInformation("Saved session to storage for user {Email}", email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save session for user {Email}", email);
                }
            }

            await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Auth.LoginSuccess, "HTML", ct);
            
            _logger.LogInformation("User {Email} logged in successfully from chatId {ChatId}", email, context.ChatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for chatId {ChatId}", context.ChatId);
            await _telegramService.SendMessageAsync(context.ChatId, TelegramMessages.Auth.LoginFailed, null, ct);
        }
    }
}
