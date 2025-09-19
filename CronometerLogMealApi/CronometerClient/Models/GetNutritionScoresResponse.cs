using System.Text.Json;

namespace CronometerLogMealApi.Models;

public class GetNutritionScoresResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
