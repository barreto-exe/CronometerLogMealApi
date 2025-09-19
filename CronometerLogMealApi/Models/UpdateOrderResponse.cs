using System.Text.Json;

namespace CronometerLogMealApi.Models;

public class UpdateOrderResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
