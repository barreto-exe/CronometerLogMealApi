namespace CronometerLogMealApi.Clients.CronometerClient.Models;

public class Food
{
    public long Id { get; set; }
    public string Name { get; set; }
    public long? MeasureId { get; set; }
    public long? DefaultMeasureId { get; set; }
    public IEnumerable<Measure>? Measures { get; set; }

    public override string ToString() => $"{Id} - {Name}";
}
