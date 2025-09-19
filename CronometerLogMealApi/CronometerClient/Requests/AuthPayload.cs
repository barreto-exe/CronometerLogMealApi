namespace CronometerLogMealApi.CronometerClient.Requests;

public record AuthPayload
{
    public long UserId { get; init; }
    public string Token { get; init; }
}
