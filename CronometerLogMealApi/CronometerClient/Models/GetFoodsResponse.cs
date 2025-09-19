using CronometerLogMealApi.CronometerClient.Models;
using System.Text.Json;

namespace CronometerLogMealApi.Models;

public class GetFoodsResponse
{
    public IEnumerable<Food>? Foods { get; set; }
}
