using System.Diagnostics;

namespace GR.OpenTelemetry.Processor.Partial;

public class Link
{
    public Link(ActivityLink activityLink)
    {
        TraceId = activityLink.Context.TraceId.ToHexString();
        SpanId = activityLink.Context.SpanId.ToHexString();
        TraceState = activityLink.Context.TraceState;
        if (activityLink.Tags != null)
            foreach (var activityLinkTag in activityLink.Tags)
            {
                var keyValue = new KeyValue
                {
                    Key = activityLinkTag.Key,
                    Value = new AnyValue(activityLinkTag.Value?.ToString())
                };
                Attributes.Add(keyValue);
            }

        Flags = activityLink.Context.TraceFlags switch
        {
            ActivityTraceFlags.None => 0,
            ActivityTraceFlags.Recorded => 1,
            _ => 0
        };
    }

    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? TraceState { get; set; }
    public List<KeyValue> Attributes { get; set; } = [];
    // TODO missing mapping?
    public uint? DroppedAttributesCount { get; set; }
    public uint? Flags { get; set; }
}