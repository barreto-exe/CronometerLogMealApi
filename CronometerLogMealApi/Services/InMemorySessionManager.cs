using System.Collections.Concurrent;
using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Models;

namespace CronometerLogMealApi.Services;

/// <summary>
/// In-memory session manager for user sessions.
/// </summary>
public class InMemorySessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, CronometerUserInfo> _sessions = new();

    public CronometerUserInfo? GetSession(string chatId)
    {
        _sessions.TryGetValue(chatId, out var userInfo);
        return userInfo;
    }

    public void SetSession(string chatId, CronometerUserInfo userInfo)
    {
        _sessions.AddOrUpdate(chatId, userInfo, (_, _) => userInfo);
    }

    public bool HasSession(string chatId)
    {
        return _sessions.ContainsKey(chatId) && 
               _sessions.TryGetValue(chatId, out var info) && 
               info != null &&
               !string.IsNullOrWhiteSpace(info.SessionKey);
    }

    public IEnumerable<KeyValuePair<string, CronometerUserInfo>> GetAllSessions()
    {
        return _sessions.ToArray();
    }
}
