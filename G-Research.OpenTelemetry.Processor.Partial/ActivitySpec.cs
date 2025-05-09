using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GR.OpenTelemetry.Processor.Partial
{
    public class ActivitySpec
    {
        public string Name { get; set; }
        public Context Context { get; set; }
        public string ParentId { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public string StatusCode { get; set; }
        public string StatusMessage { get; set; }
        public Dictionary<string, object> Attributes { get; set; }
        public List<Event> Events { get; set; }

        public enum Signal
        {
            Heartbeat,
            Stop
        }

        public ActivitySpec(Activity activity, Signal signal)
        {
            Name = activity.DisplayName;
            Context = new Context(activity.Context);
            ParentId = activity.ParentSpanId == default
                ? string.Empty
                : activity.ParentSpanId.ToString();
            StartTime = FormatTimestamp(activity.StartTimeUtc);
            EndTime = signal == Signal.Heartbeat
                ? string.Empty
                : FormatTimestamp(activity.StartTimeUtc.Add(activity.Duration));
            StatusCode = activity.Status.ToString();
            StatusMessage = activity.StatusDescription ?? string.Empty;
            Attributes = activity.TagObjects
                .ToDictionary(tag => tag.Key, tag => tag.Value)!;
            Events = activity.Events
                .Select(e => new Event
                {
                    Name = e.Name,
                    Timestamp = FormatTimestamp(e.Timestamp.DateTime),
                    Attributes = e.Tags.ToDictionary(tag => tag.Key, tag => tag.Value)!
                }).ToList();
        }

        public static string FormatTimestamp(DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff",
                CultureInfo.InvariantCulture) + " +0000 UTC";
        }

        public static string Json(ActivitySpec activitySpec)
        {
            return JsonSerializer.Serialize(activitySpec, new JsonSerializerOptions
            {
                PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
                WriteIndented = true, // For pretty printing
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // To allow special characters
            });
        }
    }

    public class Context(ActivityContext activityContext)
    {
        public string TraceId { get; set; } = activityContext.TraceId.ToString();
        public string SpanId { get; set; } = activityContext.SpanId.ToString();
    }

    public class Event
    {
        public string Name { get; set; }
        public string Timestamp { get; set; }
        public Dictionary<string, object> Attributes { get; set; }
    }

    public class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            return string.Concat(
                name.Select<char, object>((ch, i) =>
                    i > 0 && char.IsUpper(ch) ? "_" + char.ToLower(ch) : char.ToLower(ch))
            );
        }
    }
}