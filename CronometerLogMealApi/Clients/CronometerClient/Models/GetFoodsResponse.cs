using System.Text.Json;

namespace CronometerLogMealApi.Clients.CronometerClient.Models;

public class GetFoodsResponse
{
    public IEnumerable<Food>? Foods { get; set; }
}
