namespace CronometerLogMealApi.CronometerClient.Requests;

public record AddMultiServingRequest
{
    public IList<ServingPayload> Servings { get; init; } = new List<ServingPayload>();
    public AuthPayload Auth { get; init; } = new();
}
