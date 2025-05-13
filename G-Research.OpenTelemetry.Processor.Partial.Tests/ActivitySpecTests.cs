using System.Diagnostics;
using System.Text;
using Xunit;

namespace GR.OpenTelemetry.Processor.Partial.Tests
{
    public class ActivitySpecTests
    {
        [Fact]
        public void Constructor_ShouldInitializePropertiesCorrectly()
        {
            var activity = new Activity("TestActivity");
            activity.Start();
            activity.Stop();
            activity.SetStartTime(DateTime.Today);
            activity.SetEndTime(activity.StartTimeUtc.Add(activity.Duration));
            activity.AddTag("key1", "value1");
            activity.AddEvent(new ActivityEvent("TestEvent"));

            var activitySpec = new ActivitySpec(activity, ActivitySpec.Signal.Stop);

            Assert.Equal("TestActivity", activitySpec.Name);
            Assert.Equal(activity.Context.TraceId.ToString(), activitySpec.Context.TraceId);
            Assert.Equal(activity.Context.SpanId.ToString(), activitySpec.Context.SpanId);
            Assert.Equal(string.Empty, activitySpec.ParentId);
            Assert.Equal(ActivitySpec.FormatTimestamp(activity.StartTimeUtc),
                activitySpec.StartTime);
            Assert.Equal(ActivitySpec.FormatTimestamp(activity.StartTimeUtc.Add(activity.Duration)),
                activitySpec.EndTime);
            Assert.Equal("Unset", activitySpec.StatusCode);
            Assert.Equal("", activitySpec.StatusMessage);
            Assert.Single(activitySpec.Attributes);
            Assert.Single(activitySpec.Events);
        }

        [Fact]
        public void Json_ShouldSerializeActivitySpecToSnakeCaseJson()
        {
            var activity = new Activity("TestActivity");
            activity.Start();
            activity.Stop();

            var activitySpec = new ActivitySpec(activity, ActivitySpec.Signal.Stop);
            var json =
                Encoding.UTF8.GetString(
                    Convert.FromBase64String(ActivitySpec.Base64(activitySpec)));
            
            Assert.Contains("\"name\":", json);
            Assert.Contains("\"context\": {", json);
            Assert.Contains("\"trace_id\":", json);
            Assert.Contains("\"span_id\":", json);
            Assert.Contains("\"parent_id\":", json);
            Assert.Contains("\"start_time\":", json);
            Assert.Contains("\"end_time\":", json);
            Assert.Contains("\"status_code\":", json);
            Assert.Contains("\"status_message\":", json);
            Assert.Contains("\"attributes\": {", json);
            Assert.Contains("\"events\": [", json);
        }
    }
}