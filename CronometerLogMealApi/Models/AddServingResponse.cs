using System.Text.Json;

namespace CronometerLogMealApi.Models;

public class AddServingResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
