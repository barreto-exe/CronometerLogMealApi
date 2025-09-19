namespace CronometerLogMealApi.Requests;

public class LogMealRequest
{
    public string Category { get; set; }
    public IEnumerable<MealItem> Items { get; set; }
}
