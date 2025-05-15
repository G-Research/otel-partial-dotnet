using System.Diagnostics;

namespace GR.OpenTelemetry.Processor.Partial;

public class Event
{
    public Event(ActivityEvent activityEvent)
    {
        TimeUnixNano =
            SpecHelper.ToUnixTimeNanoseconds(activityEvent.Timestamp.DateTime.ToUniversalTime());
        Name = activityEvent.Name;
        foreach (var activityEventTag in activityEvent.Tags)
        {
            KeyValue keyValue = new KeyValue();
            keyValue.Key = activityEventTag.Key;
            keyValue.Value = activityEventTag.Value?.ToString();
            Attributes.Add(keyValue);
        }
    }

    public ulong? TimeUnixNano { get; set; }
    public string? Name { get; set; }
    public List<KeyValue> Attributes { get; set; } = [];
    // TODO missing mapping?
    public uint? DroppedAttributesCount { get; set; }
}