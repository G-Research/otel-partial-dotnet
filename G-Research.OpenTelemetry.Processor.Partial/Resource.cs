using System.Diagnostics;

namespace GR.OpenTelemetry.Processor.Partial;

public class Resource
{
    public List<KeyValue> Attributes { get; set; } = [];
    public int? DroppedAttributesCount { get; set; }
    public List<EntityRef> EntityRefs { get; set; } = [];
}