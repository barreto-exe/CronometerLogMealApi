namespace CronometerLogMealApi.Clients.CronometerClient.Models;

public class LoginResponse
{
    public string Result { get; set; } = string.Empty;
    public long Id { get; set; }
    public string SessionKey { get; set; } = string.Empty;
}
