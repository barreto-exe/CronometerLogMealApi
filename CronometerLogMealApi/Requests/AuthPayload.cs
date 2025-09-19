namespace CronometerLogMealApi.Requests;

public record AuthPayload
{
    public long UserId { get; init; }
    public string Token { get; init; } = string.Empty;
    public int? Api { get; init; }
    public string? Os { get; init; }
    public string? Build { get; init; }
    public string? Flavour { get; init; }
}
