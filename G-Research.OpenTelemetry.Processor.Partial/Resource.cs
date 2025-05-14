using System.Diagnostics;

namespace GR.OpenTelemetry.Processor.Partial;

public class Resource
{
    public Resource(global::OpenTelemetry.Resources.Resource resource)
    {
        foreach (var keyValuePair in resource.Attributes)
        {
            KeyValue keyValue = new KeyValue();
            keyValue.Key = keyValuePair.Key;
            
            AnyValue anyValue = new AnyValue();
            anyValue.Value = keyValuePair.Value.ToString();
            keyValue.Value = anyValue;
            
            Attributes.Add(keyValue);
        }
    }

    public List<KeyValue> Attributes { get; set; } = [];
    // TODO missing mapping?
    public int? DroppedAttributesCount { get; set; }
    // TODO missing mapping?
    public List<EntityRef> EntityRefs { get; set; } = [];
}