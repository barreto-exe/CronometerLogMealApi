using CronometerLogMealApi.Models.UserMemory;

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
    /// Text extracted from OCR (stored for combining with user corrections).
    /// </summary>
    public string? OcrExtractedText { get; set; }

    /// <summary>
    /// Pending learnings to be saved after /save confirmation.
    /// </summary>
    public List<UserMemory.PendingLearning> PendingLearnings { get; set; } = new();

    /// <summary>
    /// Current alias input term being created (for /preferences flow).
    /// </summary>
    public string? CurrentAliasInputTerm { get; set; }

    /// <summary>
    /// Current food search results for selection (for manual alias creation or re-search).
    /// </summary>
    public List<UserMemory.SearchCandidate> CurrentSearchResults { get; set; } = new();

    /// <summary>
    /// Index of the item being searched for (when user requests alternative search).
    /// </summary>
    public int? CurrentSearchItemIndex { get; set; }

    /// <summary>
    /// The alias ID pending deletion confirmation.
    /// </summary>
    public string? PendingDeleteAliasId { get; set; }

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
    /// Waiting for user to confirm or correct OCR text.
    /// </summary>
    AwaitingOCRCorrection,

    /// <summary>
    /// Currently processing the meal request.
    /// </summary>
    Processing,

    /// <summary>
    /// Waiting for user to confirm the meal before saving.
    /// </summary>
    AwaitingConfirmation,

    /// <summary>
    /// Waiting for user to confirm learning (memory save).
    /// </summary>
    AwaitingMemoryConfirmation,

    /// <summary>
    /// Waiting for user to select a preference action in /preferences menu.
    /// </summary>
    AwaitingPreferenceAction,

    /// <summary>
    /// Waiting for user to input an alias (term -> food).
    /// </summary>
    AwaitingAliasInput,

    /// <summary>
    /// Waiting for user to search for a food to create alias.
    /// </summary>
    AwaitingFoodSearch,

    /// <summary>
    /// Waiting for user to select a food from search results.
    /// </summary>
    AwaitingFoodSelection,

    /// <summary>
    /// Waiting for user to confirm alias deletion.
    /// </summary>
    AwaitingAliasDeleteConfirm,

    /// <summary>
    /// Waiting for user to select from multiple food search results.
    /// </summary>
    AwaitingFoodSearchSelection
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
