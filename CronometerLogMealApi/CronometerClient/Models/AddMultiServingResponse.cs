using System.Text.Json;

namespace CronometerLogMealApi.Models;

public class AddMultiServingResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
