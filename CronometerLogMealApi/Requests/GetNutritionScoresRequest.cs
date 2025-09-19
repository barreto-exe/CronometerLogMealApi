namespace CronometerLogMealApi.Requests;

public record GetNutritionScoresRequest
{
    public string StartDay { get; init; } = string.Empty;
    public string EndDay { get; init; } = string.Empty;
    public IList<long>? ServingIds { get; init; }
    public string? Supplements { get; init; }
    public string? AllTime { get; init; }
    public string Type { get; init; } = "Complete";
    public AuthPayload Auth { get; init; } = new();
    public long? LastSeen { get; init; }
    public ConfigPayload? Config { get; init; }
}
