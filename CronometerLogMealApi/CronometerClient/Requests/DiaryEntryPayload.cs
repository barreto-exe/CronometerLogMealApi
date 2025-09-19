namespace CronometerLogMealApi.CronometerClient.Requests;

public record DiaryEntryPayload
{
    public int Order { get; init; }
    public string Day { get; init; } = string.Empty;
    public string? Source { get; init; }
    public long UserId { get; init; }
    public long ServingId { get; init; }
    public string Type { get; init; } = "Serving";
    public long FoodId { get; init; }
    public long? MeasureId { get; init; }
    public double? Grams { get; init; }
    public long? TranslationId { get; init; }
}
