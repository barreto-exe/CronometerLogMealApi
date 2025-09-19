namespace CronometerLogMealApi.Requests;

public class LogMealRequest
{
    public string Category { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public IEnumerable<MealItem> Items { get; set; }
}
