using System.Diagnostics;
using Xunit;

namespace GR.OpenTelemetry.Processor.Partial.Tests
{
    public class InstrumentationScopeTests
    {
        [Fact]
        public void Constructor_ShouldMapActivitySourcePropertiesCorrectly()
        {
            var activitySource = new ActivitySource("TestSource", "1.0.0");
            var activityListener = new ActivityListener
            {
                ShouldListenTo = s => true,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                    ActivitySamplingResult.AllData,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllData
            };
            ActivitySource.AddActivityListener(activityListener);
            var activity = activitySource.StartActivity("TestActivity");
            
            var instrumentationScope = new InstrumentationScope(activity!);

            Assert.Equal("TestSource", instrumentationScope.Name);
            Assert.Equal("1.0.0", instrumentationScope.Version);
        }

        // TODO enable this after version upgrade
        /*
        [Fact]
        public void Constructor_ShouldMapAttributesCorrectly()
        {
            var activitySource = new ActivitySource("TestSource", "1.0.0");
            var activityListener = new ActivityListener
            {
                ShouldListenTo = s => true,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                    ActivitySamplingResult.AllData,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllData
            };
            ActivitySource.AddActivityListener(activityListener);
            var activity = activitySource.StartActivity("TestActivity");
            
            activity?.SetTag("key1", "value1");
            activity?.SetTag("key2", 123);

            var instrumentationScope = new InstrumentationScope(activity!);

            Assert.NotNull(instrumentationScope.Attributes);
            Assert.Equal(2, instrumentationScope.Attributes.Count);
            Assert.Contains(instrumentationScope.Attributes,
                attr => attr is { Key: "key1", Value: "value1" });
            Assert.Contains(instrumentationScope.Attributes,
                attr => attr is { Key: "key2", Value: "123" });
        }
        */
    }
}