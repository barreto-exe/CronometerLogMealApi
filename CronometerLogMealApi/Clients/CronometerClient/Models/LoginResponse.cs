using System.Text.Json;

namespace CronometerLogMealApi.Clients.CronometerClient.Models;

public class LoginResponse
{
    public string Result { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string SessionKey { get; set; } = string.Empty;
}
