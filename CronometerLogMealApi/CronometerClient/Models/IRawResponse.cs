using System.Text.Json;

namespace CronometerLogMealApi.Models;

public interface IRawResponse
{
    JsonElement Raw { get; set; }
}
