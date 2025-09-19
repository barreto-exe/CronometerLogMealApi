namespace CronometerLogMealApi.Requests;

public record ServingPayload
{
    public int Order { get; init; }
    public string Day { get; init; } = string.Empty;
    public long UserId { get; init; }
    public string Type { get; init; } = "Serving";
    public long FoodId { get; init; }
    public long? MeasureId { get; init; }
    public double? Grams { get; init; }
    public long? ServingId { get; init; }
    public long? TranslationId { get; init; }
}
