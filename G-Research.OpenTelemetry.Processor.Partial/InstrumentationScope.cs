using System.Diagnostics;

namespace GR.OpenTelemetry.Processor.Partial;

public class InstrumentationScope
{
    public InstrumentationScope(Activity activity)
    {
        Name = activity.Source.Name;
        
        Version = activity.Source.Version;
        
        activity.Source.Tags?.ToList().ForEach(tag =>
        {
            KeyValue keyValue = new KeyValue();
            keyValue.Key = tag.Key;
            
            AnyValue anyValue = new AnyValue();
            anyValue.Value = tag.Value?.ToString();
            keyValue.Value = anyValue;
            Attributes.Add(keyValue);
        });
    }

    public string? Name { get; set; }
    public string? Version { get; set; }
    public List<KeyValue> Attributes { get; set; } = [];
    // TODO missing mapping?
    public uint? DroppedAttributesCount { get; set; }
}