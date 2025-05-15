using System.Diagnostics;
using Xunit;

namespace GR.OpenTelemetry.Processor.Partial.Tests
{
    public class SpanTests
    {
        [Fact]
        public void Constructor_ShouldMapActivityPropertiesCorrectly()
        {
            var activity = new Activity("TestActivity");
            activity.SetIdFormat(ActivityIdFormat.W3C);
            activity.Start();
            activity.Stop();

            var span = new Span(activity, TracesData.Signal.Stop);

            Assert.Equal(activity.TraceId.ToHexString(), span.TraceId);
            Assert.Equal(activity.SpanId.ToHexString(), span.SpanId);
            Assert.Equal(activity.Status.ToString(), span.TraceState);
            Assert.Equal(activity.ParentSpanId.ToHexString(), span.ParentSpanId);
            Assert.Equal((uint)activity.ActivityTraceFlags, span.Flags);
            Assert.Equal(activity.DisplayName, span.Name);
            Assert.Equal(SpanKind.SpanKindInternal, span.Kind); // Default kind
            Assert.Equal(SpecHelper.ToUnixTimeNanoseconds(activity.StartTimeUtc), span.StartTimeUnixNano);
            Assert.Equal(SpecHelper.ToUnixTimeNanoseconds(activity.StartTimeUtc.Add(activity.Duration)), span.EndTimeUnixNano);
        }

        [Fact]
        public void Constructor_ShouldMapAttributesCorrectly()
        {
            var activity = new Activity("TestActivity");
            activity.AddTag("key1", "value1");
            activity.AddTag("key2", 123);
            activity.Start();
            activity.Stop();

            var span = new Span(activity, TracesData.Signal.Stop);

            Assert.NotNull(span.Attributes);
            Assert.Equal(2, span.Attributes.Count);
            Assert.Contains(span.Attributes, attr => attr.Key == "key1" && attr.Value == "value1");
            Assert.Contains(span.Attributes, attr => attr.Key == "key2" && attr.Value == "123");
        }
    }
}