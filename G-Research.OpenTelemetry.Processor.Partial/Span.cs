using System.Diagnostics;
using GR.OpenTelemetry.Processor.Partial;

public class Span
{
    public Span(Activity activity, TracesData.Signal signal)
    {
        TraceId = activity.TraceId.ToHexString();
        
        SpanId = activity.SpanId.ToHexString();
        
        TraceState = activity.Status.ToString();
        
        ParentSpanId = activity.ParentSpanId.ToHexString();
        
        Flags = (uint) activity.ActivityTraceFlags;
        
        Name = activity.DisplayName;

        Kind = activity.Kind switch
        {
            ActivityKind.Internal => SpanKind.SpanKindInternal,
            ActivityKind.Client => SpanKind.SpanKindClient,
            ActivityKind.Server => SpanKind.SpanKindServer,
            ActivityKind.Producer => SpanKind.SpanKindProducer,
            ActivityKind.Consumer => SpanKind.SpanKindConsumer,
            _ => SpanKind.SpanKindUnspecified

        };
        
        StartTimeUnixNano = SpecHelper.ToUnixTimeNanoseconds(activity.StartTimeUtc);
        
        EndTimeUnixNano = signal == TracesData.Signal.Heartbeat
            ? null
            : SpecHelper.ToUnixTimeNanoseconds(activity.StartTimeUtc.Add(activity.Duration));
        
        foreach (var activityTagObject in activity.TagObjects)
        {
            var keyValue = new KeyValue
            {
                Key = activityTagObject.Key,
                Value = new AnyValue(activityTagObject.Value?.ToString())
            };
            Attributes.Add(keyValue);
        }
        
        foreach (var activityEvent in activity.Events)
        {
            Events.Add(new Event(activityEvent));
        }
        
        foreach (var activityLink in activity.Links)
        {
            Links.Add(new Link(activityLink));
        }
        
        Status = new Status(activity.Status, activity.StatusDescription);
    }

    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? TraceState { get; set; }
    public string? ParentSpanId { get; set; }
    public uint? Flags { get; set; }
    public string? Name { get; set; }
    public SpanKind? Kind { get; set; }
    public ulong? StartTimeUnixNano { get; set; }
    public ulong? EndTimeUnixNano { get; set; }
    public List<KeyValue> Attributes { get; set; } = [];
    // TODO missing mapping?
    public uint? DroppedAttributesCount { get; set; }
    public List<Event> Events { get; set; } = [];
    // TODO missing mapping?
    public uint? DroppedEventsCount { get; set; }
    public List<Link> Links { get; set; } = [];
    // TODO missing mapping?
    public uint? DroppedLinksCount { get; set; }
    public Status? Status { get; set; }
}