using System.Text.Json;

namespace CronometerLogMealApi.Clients.CronometerClient.Models;

public interface IRawResponse
{
    JsonElement Raw { get; set; }
}
