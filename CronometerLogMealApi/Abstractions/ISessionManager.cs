using CronometerLogMealApi.Models;

namespace CronometerLogMealApi.Abstractions;

/// <summary>
/// Abstraction for managing user sessions.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Gets or creates a user session.
    /// </summary>
    CronometerUserInfo? GetSession(string chatId);

    /// <summary>
    /// Updates or creates a user session.
    /// </summary>
    void SetSession(string chatId, CronometerUserInfo userInfo);

    /// <summary>
    /// Checks if a user has an active session.
    /// </summary>
    bool HasSession(string chatId);

    /// <summary>
    /// Gets all active sessions.
    /// </summary>
    IEnumerable<KeyValuePair<string, CronometerUserInfo>> GetAllSessions();
}
