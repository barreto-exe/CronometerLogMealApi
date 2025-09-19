using System.Text.Json;

namespace CronometerLogMealApi.Models;

public class FindFoodResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
