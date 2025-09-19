using System.Text.Json;

namespace CronometerLogMealApi.Models;

public class GetFoodsResponse : IRawResponse
{
    public JsonElement Raw { get; set; }
}
