using System.Text.Json;

namespace CronometerLogMealApi.Clients.CronometerClient.Models;

public class UpdateOrderResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
