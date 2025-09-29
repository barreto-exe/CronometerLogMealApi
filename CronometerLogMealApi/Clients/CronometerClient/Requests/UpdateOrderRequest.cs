namespace CronometerLogMealApi.Clients.CronometerClient.Requests;

public record UpdateOrderRequest
{
    public IList<DiaryEntryPayload> DiaryEntries { get; init; } = new List<DiaryEntryPayload>();
    public AuthPayload Auth { get; init; } = new();
}
