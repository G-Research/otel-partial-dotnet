using System.Diagnostics;

namespace GR.OpenTelemetry.Processor.Partial;

public class Resource
{
    public List<KeyValue> Attributes { get; set; } = [];
    // TODO missing mapping?
    public int? DroppedAttributesCount { get; set; }
    // TODO missing mapping?
    public List<EntityRef> EntityRefs { get; set; } = [];
}