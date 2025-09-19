namespace CronometerLogMealApi.CronometerClient.Requests;

public record AddServingRequest
{
    public ServingPayload Serving { get; init; } = new();
    public AuthPayload Auth { get; init; } = new();
}
