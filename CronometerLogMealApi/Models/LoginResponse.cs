using System.Text.Json;

namespace CronometerLogMealApi.Models;

public class LoginResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
