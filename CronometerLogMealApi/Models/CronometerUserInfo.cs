namespace CronometerLogMealApi.Models;

public class CronometerUserInfo
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public long? UserId { get; set; }
    public string? SessionKey { get; set; }

    /// <summary>
    /// Active conversation session for this user, null if no session is active.
    /// </summary>
    public ConversationSession? Conversation { get; set; }
}
