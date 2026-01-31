using CronometerLogMealApi.Models;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Interface for conversation state processors.
/// Each processor handles a specific conversation state.
/// </summary>
public interface IStateProcessor
{
    /// <summary>
    /// Gets the conversation state this processor handles.
    /// </summary>
    ConversationState HandledState { get; }

    /// <summary>
    /// Processes the user message in the current conversation state.
    /// </summary>
    Task ProcessAsync(StateContext context, CancellationToken ct);
}

/// <summary>
/// Context for state processors.
/// </summary>
public class StateContext
{
    /// <summary>
    /// The Telegram chat ID.
    /// </summary>
    public required string ChatId { get; init; }

    /// <summary>
    /// The user's message text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// The user session info.
    /// </summary>
    public required CronometerUserInfo UserInfo { get; init; }

    /// <summary>
    /// The current conversation session.
    /// </summary>
    public ConversationSession Conversation => UserInfo.Conversation!;
}
