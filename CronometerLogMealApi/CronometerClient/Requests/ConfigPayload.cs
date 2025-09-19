using System.Text.Json.Serialization;

namespace CronometerLogMealApi.CronometerClient.Requests;

public record ConfigPayload
{
    [JsonPropertyName("call_version")] public int? CallVersion { get; init; }
}
