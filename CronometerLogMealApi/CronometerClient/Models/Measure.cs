namespace CronometerLogMealApi.CronometerClient.Models;

public class Measure
{
    public long Id { get; set; }
    public string Name { get; set; }
    public double Value { get; set; } = 0;
}
