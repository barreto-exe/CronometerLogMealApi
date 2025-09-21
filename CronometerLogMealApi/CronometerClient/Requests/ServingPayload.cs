namespace CronometerLogMealApi.CronometerClient.Requests;

public record ServingPayload
{
    public int Order { get; init; }
    public string Day { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public long UserId { get; init; }
    public string Type { get; init; } = "Serving";
    public long FoodId { get; set; }
    public long? MeasureId { get; set; }
    public double? Grams { get; set; }
}
