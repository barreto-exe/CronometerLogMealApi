namespace CronometerLogMealApi.CronometerClient.Requests;

public record GetFoodsRequest
{
    public IList<long> Ids { get; init; } = new List<long>();
    public AuthPayload Auth { get; init; } = new();
}
