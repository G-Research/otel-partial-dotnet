using System.Diagnostics;
using OpenTelemetry.Resources;
using Xunit;

namespace GR.OpenTelemetry.Processor.Partial.Tests
{
    public class TracesDataTests
    {
        [Fact]
        public void Json_ShouldSerializeActivitySpecToSnakeCaseJson()
        {
            var activity = new Activity("TestActivity");
            activity.Start();
            activity.Stop();

            var tracesData = new TracesData(activity, ResourceBuilder.CreateDefault().Build(),
                TracesData.Signal.Stop);
            var json = SpecHelper.Json(tracesData);

            // TODO fix this
            Assert.Contains("\"resource_spans\":[", json);
            Assert.Contains("\"resource\":{", json);
            Assert.Contains("\"scope_spans\":[", json);
            Assert.Contains("\"trace_id\":", json);
            Assert.Contains("\"span_id\":", json);
            Assert.Contains("\"trace_state\":", json);
            Assert.Contains("\"parent_span_id\":", json);
            Assert.Contains("\"flags\":", json);
            Assert.Contains("\"name\":", json);
            Assert.Contains("\"kind\":", json);
            Assert.Contains("\"start_time_unix_nano\":", json);
            Assert.Contains("\"end_time_unix_nano\":", json);
            Assert.Contains("\"attributes\":[", json);
            // TODO enable this once mapped
            // Assert.Contains("\"dropped_attributes_count\":", json);
            Assert.Contains("\"events\":[", json);
            // TODO enable this once mapped
            // Assert.Contains("\"dropped_events_count\":", json);
            Assert.Contains("\"links\":[", json);
            // TODO enable this once mapped
            // Assert.Contains("\"dropped_links_count\":", json);
            Assert.Contains("\"status\":{", json);
            // TODO figure out how to set this
            // Assert.Contains("\"message\":", json);
            Assert.Contains("\"code\":", json);
        }
    }
}