namespace CronometerLogMealApi.Models;

/// <summary>
/// Represents an active conversation session for meal logging.
/// </summary>
public class ConversationSession
{
    /// <summary>
    /// Current state of the conversation.
    /// </summary>
    public ConversationState State { get; set; } = ConversationState.Idle;

    /// <summary>
    /// The pending meal request being built through the conversation.
    /// </summary>
    public Requests.LogMealRequest? PendingMealRequest { get; set; }

    /// <summary>
    /// History of messages in this conversation for context building.
    /// </summary>
    public List<ConversationMessage> MessageHistory { get; set; } = new();

    /// <summary>
    /// Items that need clarification before logging.
    /// </summary>
    public List<ClarificationItem> PendingClarifications { get; set; } = new();

    /// <summary>
    /// When the session was started.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the last activity occurred (for timeout purposes).
    /// </summary>
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// Original meal description provided by the user.
    /// </summary>
    public string? OriginalDescription { get; set; }

    /// <summary>
    /// List of validated foods ready to be logged (populated after DB validation).
    /// </summary>
    public List<ValidatedMealItem> ValidatedFoods { get; set; } = new();

    /// <summary>
    /// Session timeout duration (default: 10 minutes).
    /// </summary>
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Checks if this session has expired due to inactivity.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow - LastActivityAt > SessionTimeout;

    /// <summary>
    /// Updates the last activity timestamp to now.
    /// </summary>
    public void Touch()
    {
        LastActivityAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Possible states for a conversation session.
/// </summary>
public enum ConversationState
{
    /// <summary>
    /// No active session, waiting for /start command.
    /// </summary>
    Idle,

    /// <summary>
    /// Waiting for the user to describe their meal.
    /// </summary>
    AwaitingMealDescription,

    /// <summary>
    /// Waiting for user response to clarification questions.
    /// </summary>
    AwaitingClarification,

    /// <summary>
    /// Currently processing the meal request.
    /// </summary>
    Processing,

    /// <summary>
    /// Waiting for user to confirm the meal before saving.
    /// </summary>
    AwaitingConfirmation
}

/// <summary>
/// Represents a single message in the conversation history.
/// </summary>
public class ConversationMessage
{
    /// <summary>
    /// Role of the message sender: "user" or "assistant".
    /// </summary>
    public string Role { get; set; } = "user";

    /// <summary>
    /// Content of the message.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// When the message was sent.
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Represents an item requiring clarification from the user.
/// </summary>
public class ClarificationItem
{
    /// <summary>
    /// Type of clarification needed.
    /// </summary>
    public ClarificationType Type { get; set; }

    /// <summary>
    /// Name of the item requiring clarification.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// The question to ask the user.
    /// </summary>
    public string Question { get; set; } = string.Empty;
}

/// <summary>
/// Types of clarifications that may be needed.
/// </summary>
public enum ClarificationType
{
    /// <summary>
    /// Item is missing weight/quantity information.
    /// </summary>
    MissingWeight,

    /// <summary>
    /// Item is missing size information (small/medium/large).
    /// </summary>
    MissingSize,

    /// <summary>
    /// The specified food was not found in Cronometer's database.
    /// </summary>
    FoodNotFound,

    /// <summary>
    /// The unit is ambiguous (e.g., "cucharada" could be tbsp or tsp).
    /// </summary>
    AmbiguousUnit
}
