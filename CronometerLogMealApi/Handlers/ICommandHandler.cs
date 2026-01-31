using CronometerLogMealApi.Models;

namespace CronometerLogMealApi.Handlers;

/// <summary>
/// Base interface for all Telegram command handlers.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Checks if this handler can process the given command.
    /// </summary>
    bool CanHandle(string? command);

    /// <summary>
    /// Handles the command.
    /// </summary>
    Task HandleAsync(CommandContext context, CancellationToken ct);
}

/// <summary>
/// Context passed to command handlers.
/// </summary>
public class CommandContext
{
    /// <summary>
    /// The Telegram chat ID.
    /// </summary>
    public required string ChatId { get; init; }

    /// <summary>
    /// The command text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// The user session info (if authenticated).
    /// </summary>
    public CronometerUserInfo? UserInfo { get; set; }

    /// <summary>
    /// Indicates if the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => UserInfo != null && !string.IsNullOrWhiteSpace(UserInfo.SessionKey);
}
