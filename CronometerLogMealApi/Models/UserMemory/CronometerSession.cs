using Google.Cloud.Firestore;

namespace CronometerLogMealApi.Models.UserMemory;

/// <summary>
/// Represents a persisted Cronometer session for a Telegram user.
/// Allows users to stay logged in across server restarts/deploys.
/// </summary>
[FirestoreData]
public class CronometerSession
{
    /// <summary>
    /// Unique identifier for the session document.
    /// </summary>
    [FirestoreProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The user's Telegram chat ID.
    /// </summary>
    [FirestoreProperty("telegramChatId")]
    public string TelegramChatId { get; set; } = string.Empty;

    /// <summary>
    /// The Cronometer user ID.
    /// </summary>
    [FirestoreProperty("cronometerUserId")]
    public long CronometerUserId { get; set; }

    /// <summary>
    /// The Cronometer session key (auth token).
    /// </summary>
    [FirestoreProperty("sessionKey")]
    public string SessionKey { get; set; } = string.Empty;

    /// <summary>
    /// The user's email (for reference).
    /// </summary>
    [FirestoreProperty("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// When this session was created (first login).
    /// </summary>
    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this session was last updated (last login or refresh).
    /// </summary>
    [FirestoreProperty("lastUpdatedAt")]
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this session is still active/valid.
    /// </summary>
    [FirestoreProperty("isActive")]
    public bool IsActive { get; set; } = true;
}
