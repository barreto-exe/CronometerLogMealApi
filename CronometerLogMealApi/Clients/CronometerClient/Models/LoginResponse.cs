using System.Text.Json;

namespace CronometerLogMealApi.Clients.CronometerClient.Models;

public class LoginResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
