namespace GR.OpenTelemetry.Processor.Partial;

public class EntityRef
{
    public string? SchemaUrl { get; set; }
    public string? Type { get; set; }
    public List<string> IdKeys { get; set; } = [];
    public string? DescriptionKeys { get; set; }
}