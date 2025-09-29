using System.Text.Json;

namespace CronometerLogMealApi.Clients.CronometerClient.Models;

public class FindFoodResponse
{
    public IEnumerable<Food> Foods { get; set; }
}
