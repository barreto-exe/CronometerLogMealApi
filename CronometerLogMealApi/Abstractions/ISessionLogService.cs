using CronometerLogMealApi.Models.Logging;

namespace CronometerLogMealApi.Abstractions;

/// <summary>
/// Service for logging session events to Firestore.
/// Accumulates events in-memory and flushes to Firestore on session end.
/// </summary>
public interface ISessionLogService
{
    /// <summary>
    /// Starts a new session log. Call when session begins (/start or photo received).
    /// </summary>
    void StartSession(string chatId);

    /// <summary>
    /// Logs an incoming user message.
    /// </summary>
    void LogUserMessage(string chatId, string message);

    /// <summary>
    /// Logs an outgoing bot response.
    /// </summary>
    void LogBotResponse(string chatId, string message);

    /// <summary>
    /// Logs an LLM request/response cycle.
    /// </summary>
    void LogLlmCall(string chatId, string userInput, string? rawResponse, long durationMs, bool parseSuccess);

    /// <summary>
    /// Logs an HTTP request/response to external APIs (Cronometer, Telegram file downloads, etc.)
    /// </summary>
    void LogHttpCall(string chatId, string service, string operation, object? request, object? response, int? statusCode, long durationMs);

    /// <summary>
    /// Logs a state transition.
    /// </summary>
    void LogStateChange(string chatId, string fromState, string toState, string? trigger = null);

    /// <summary>
    /// Logs an OCR event.
    /// </summary>
    void LogOcr(string chatId, string? extractedText, long durationMs, bool success);

    /// <summary>
    /// Logs a validation event.
    /// </summary>
    void LogValidation(string chatId, int itemsValidated, int itemsNotFound, List<string>? notFoundItems = null);

    /// <summary>
    /// Logs a generic event.
    /// </summary>
    void LogEvent(string chatId, string eventType, Dictionary<string, object>? data = null);

    /// <summary>
    /// Logs an error.
    /// </summary>
    void LogError(string chatId, string message, string? exceptionType = null, string? stackTrace = null);

    /// <summary>
    /// Ends the session and flushes all events to Firestore.
    /// </summary>
    Task EndSessionAsync(string chatId, string status, int itemsLogged = 0, string? originalDescription = null, CancellationToken ct = default);

    /// <summary>
    /// Gets the current session log for a chat (for debugging/inspection).
    /// </summary>
    SessionLog? GetCurrentSession(string chatId);
}
