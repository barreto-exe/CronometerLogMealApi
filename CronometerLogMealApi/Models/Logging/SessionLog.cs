using Google.Cloud.Firestore;

namespace CronometerLogMealApi.Models.Logging;

/// <summary>
/// Represents a complete session log document in Firestore.
/// </summary>
[FirestoreData]
public class SessionLog
{
    [FirestoreProperty("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [FirestoreProperty("chatId")]
    public string ChatId { get; set; } = string.Empty;

    [FirestoreProperty("startedAt")]
    public DateTime StartedAt { get; set; }

    [FirestoreProperty("endedAt")]
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Session end status: "completed", "cancelled", "expired", "error"
    /// </summary>
    [FirestoreProperty("status")]
    public string Status { get; set; } = "active";

    [FirestoreProperty("originalDescription")]
    public string? OriginalDescription { get; set; }

    [FirestoreProperty("itemsLogged")]
    public int ItemsLogged { get; set; }

    [FirestoreProperty("events")]
    public List<SessionEvent> Events { get; set; } = new();
}

/// <summary>
/// Represents a single event within a session.
/// </summary>
[FirestoreData]
public class SessionEvent
{
    [FirestoreProperty("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Event types: "user_message", "bot_response", "llm_request", "llm_response", 
    /// "http_request", "http_response", "state_change", "error", "ocr", "validation"
    /// </summary>
    [FirestoreProperty("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Dynamic payload for the event (varies by type)
    /// </summary>
    [FirestoreProperty("data")]
    public Dictionary<string, object> Data { get; set; } = new();
}
