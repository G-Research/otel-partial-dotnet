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
                KeyValue keyValue = new KeyValue();
                keyValue.Key = activityLinkTag.Key;

                AnyValue anyValue = new AnyValue();
                anyValue.Value = activityLinkTag.Value?.ToString();
                keyValue.Value = anyValue;
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