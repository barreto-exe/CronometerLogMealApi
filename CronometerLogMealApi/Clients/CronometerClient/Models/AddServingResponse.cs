using System.Text.Json;

namespace CronometerLogMealApi.Clients.CronometerClient.Models;

public class AddServingResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
