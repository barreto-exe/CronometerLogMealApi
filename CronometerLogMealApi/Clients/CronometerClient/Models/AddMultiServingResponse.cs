using System.Text.Json;

namespace CronometerLogMealApi.Clients.CronometerClient.Models;

public class AddMultiServingResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
