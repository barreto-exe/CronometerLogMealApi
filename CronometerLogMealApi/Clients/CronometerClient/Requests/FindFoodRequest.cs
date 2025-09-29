namespace CronometerLogMealApi.Clients.CronometerClient.Requests;

public record FindFoodRequest
{
    public string Query { get; init; } = string.Empty;
    public string? Tab { get; set; }
    public AuthPayload Auth { get; init; } = new();
}
