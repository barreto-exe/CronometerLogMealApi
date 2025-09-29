using System.Text.Json;

namespace CronometerLogMealApi.Clients.CronometerClient.Models;

public class GetNutritionScoresResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
