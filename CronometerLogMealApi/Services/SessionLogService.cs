using System.Collections.Concurrent;
using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Models.Logging;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Grpc.Auth;
using Microsoft.Extensions.Options;

namespace CronometerLogMealApi.Services;

/// <summary>
/// Service that accumulates session events in-memory and flushes to Firestore on session end.
/// </summary>
public class SessionLogService : ISessionLogService
{
    private readonly FirestoreDb _db;
    private readonly ILogger<SessionLogService> _logger;
    private readonly ConcurrentDictionary<string, SessionLog> _activeSessions = new();

    private const string SessionLogsCollection = "sessionLogs";

    public SessionLogService(IOptions<FirebaseOptions> options, ILogger<SessionLogService> logger)
    {
        _logger = logger;
        var firebaseOptions = options.Value;

        if (string.IsNullOrWhiteSpace(firebaseOptions.ProjectId))
        {
            throw new ArgumentException("Firebase ProjectId is required for SessionLogService.");
        }

        if (firebaseOptions.HasInlineCredentials)
        {
            _db = CreateDbFromInlineCredentials(firebaseOptions);
        }
        else if (!string.IsNullOrWhiteSpace(firebaseOptions.CredentialsPath))
        {
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", firebaseOptions.CredentialsPath);
            _db = FirestoreDb.Create(firebaseOptions.ProjectId);
        }
        else
        {
            _db = FirestoreDb.Create(firebaseOptions.ProjectId);
        }

        _logger.LogInformation("SessionLogService initialized for project: {ProjectId}", firebaseOptions.ProjectId);
    }

    private static FirestoreDb CreateDbFromInlineCredentials(FirebaseOptions options)
    {
        var privateKey = options.PrivateKey.Replace("\\n", "\n");
        var credential = GoogleCredential.FromServiceAccountCredential(
            new ServiceAccountCredential(
                new ServiceAccountCredential.Initializer(options.ClientEmail)
                {
                    ProjectId = options.ProjectId
                }.FromPrivateKey(privateKey)));

        var clientBuilder = new FirestoreClientBuilder
        {
            ChannelCredentials = credential.ToChannelCredentials()
        };

        return FirestoreDb.Create(options.ProjectId, clientBuilder.Build());
    }

    public void StartSession(string chatId)
    {
        var sessionId = $"{chatId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
        var session = new SessionLog
        {
            SessionId = sessionId,
            ChatId = chatId,
            StartedAt = DateTime.UtcNow,
            Status = "active"
        };

        _activeSessions[chatId] = session;
        AddEvent(chatId, "session_start", null);
        _logger.LogDebug("Started session log {SessionId} for chat {ChatId}", sessionId, chatId);
    }

    public void LogUserMessage(string chatId, string message)
    {
        AddEvent(chatId, "user_message", new Dictionary<string, object>
        {
            ["message"] = message
        });
    }

    public void LogBotResponse(string chatId, string message)
    {
        AddEvent(chatId, "bot_response", new Dictionary<string, object>
        {
            ["message"] = message
        });
    }

    public void LogLlmCall(string chatId, string userInput, string? rawResponse, long durationMs, bool parseSuccess)
    {
        AddEvent(chatId, "llm_call", new Dictionary<string, object>
        {
            ["userInput"] = userInput,
            ["rawResponse"] = rawResponse ?? "",
            ["durationMs"] = durationMs,
            ["parseSuccess"] = parseSuccess
        });
    }

    public void LogHttpCall(string chatId, string service, string operation, object? request, object? response, int? statusCode, long durationMs)
    {
        var data = new Dictionary<string, object>
        {
            ["service"] = service,
            ["operation"] = operation,
            ["durationMs"] = durationMs
        };

        if (request != null)
            data["request"] = SerializeForLog(request);
        if (response != null)
            data["response"] = SerializeForLog(response);
        if (statusCode.HasValue)
            data["statusCode"] = statusCode.Value;

        AddEvent(chatId, "http_call", data);
    }

    public void LogStateChange(string chatId, string fromState, string toState, string? trigger = null)
    {
        var data = new Dictionary<string, object>
        {
            ["fromState"] = fromState,
            ["toState"] = toState
        };
        if (trigger != null)
            data["trigger"] = trigger;

        AddEvent(chatId, "state_change", data);
    }

    public void LogOcr(string chatId, string? extractedText, long durationMs, bool success)
    {
        AddEvent(chatId, "ocr", new Dictionary<string, object>
        {
            ["extractedText"] = extractedText ?? "",
            ["durationMs"] = durationMs,
            ["success"] = success
        });
    }

    public void LogValidation(string chatId, int itemsValidated, int itemsNotFound, List<string>? notFoundItems = null)
    {
        var data = new Dictionary<string, object>
        {
            ["itemsValidated"] = itemsValidated,
            ["itemsNotFound"] = itemsNotFound
        };
        if (notFoundItems != null && notFoundItems.Count > 0)
            data["notFoundItems"] = notFoundItems;

        AddEvent(chatId, "validation", data);
    }

    public void LogEvent(string chatId, string eventType, Dictionary<string, object>? data = null)
    {
        AddEvent(chatId, eventType, data);
    }

    public void LogError(string chatId, string message, string? exceptionType = null, string? stackTrace = null)
    {
        var data = new Dictionary<string, object>
        {
            ["message"] = message
        };
        if (exceptionType != null)
            data["exceptionType"] = exceptionType;
        if (stackTrace != null)
            data["stackTrace"] = stackTrace;

        AddEvent(chatId, "error", data);
    }

    public async Task EndSessionAsync(string chatId, string status, int itemsLogged = 0, string? originalDescription = null, CancellationToken ct = default)
    {
        if (!_activeSessions.TryRemove(chatId, out var session))
        {
            _logger.LogWarning("No active session found for chat {ChatId} when ending session", chatId);
            return;
        }

        session.EndedAt = DateTime.UtcNow;
        session.Status = status;
        session.ItemsLogged = itemsLogged;
        session.OriginalDescription = originalDescription;

        AddEventToSession(session, "session_end", new Dictionary<string, object>
        {
            ["status"] = status,
            ["itemsLogged"] = itemsLogged
        });

        try
        {
            var docRef = _db.Collection(SessionLogsCollection).Document(session.SessionId);
            await docRef.SetAsync(session, cancellationToken: ct);
            _logger.LogInformation("Saved session log {SessionId} with {EventCount} events", session.SessionId, session.Events.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save session log {SessionId} to Firestore", session.SessionId);
        }
    }

    public SessionLog? GetCurrentSession(string chatId)
    {
        _activeSessions.TryGetValue(chatId, out var session);
        return session;
    }

    private void AddEvent(string chatId, string eventType, Dictionary<string, object>? data)
    {
        if (!_activeSessions.TryGetValue(chatId, out var session))
        {
            // No active session, silently ignore (session may not have started yet or already ended)
            return;
        }

        AddEventToSession(session, eventType, data);
    }

    private static void AddEventToSession(SessionLog session, string eventType, Dictionary<string, object>? data)
    {
        var evt = new SessionEvent
        {
            Timestamp = DateTime.UtcNow,
            Type = eventType,
            Data = data ?? new Dictionary<string, object>()
        };

        lock (session.Events)
        {
            session.Events.Add(evt);
        }
    }

    private static object SerializeForLog(object obj)
    {
        // Return simple string representation for logging
        // Avoid complex serialization that could fail
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(obj);
        }
        catch
        {
            return obj.ToString() ?? "";
        }
    }
}
