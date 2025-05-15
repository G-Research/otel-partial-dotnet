using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace GR.OpenTelemetry.Processor.Partial.Tests
{
    public class PartialActivityProcessorTests
    {
        private List<LogRecord> exportedLogs = [];
        private InMemoryExporter<LogRecord> logExporter;
        private PartialActivityProcessor _processor;
        private const int HeartbeatIntervalMilliseconds = 1000;

        public PartialActivityProcessorTests()
        {
            logExporter = new InMemoryExporter<LogRecord>(exportedLogs);
            _processor = new PartialActivityProcessor(logExporter, HeartbeatIntervalMilliseconds);
        }

        // TODO fix this test
        [Fact]
        public void Log_ShouldContainDefinedResource()
        {
            ActivitySource activitySource = new("activitySourceTest");
            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = source => true,
                SampleUsingParentId = (ref ActivityCreationOptions<string> options) =>
                    ActivitySamplingResult.AllData,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                    ActivitySamplingResult.AllData,
            });

            var tracerProvider = Sdk.CreateTracerProviderBuilder()
                .AddSource("activitySourceTest")
                .ConfigureResource(configure => { configure.AddService("Test"); })
                .AddProcessor(_processor)
                .Build();

            var activity = activitySource.CreateActivity("activityTest", ActivityKind.Internal);
            activity?.Start();
            activity?.Stop();

            Assert.NotEmpty(exportedLogs);
        }

        [Fact]
        public void Constructor_ShouldInitializeFields()
        {
            Assert.NotNull(_processor);
        }

        [Fact]
        public void OnStart_ShouldExportHeartbeatLog()
        {
            var activity = new Activity("TestActivity");

            _processor.OnStart(activity);

            Assert.Contains(activity.SpanId, _processor.ActiveActivities);
            Assert.Single(exportedLogs);
        }

        [Fact]
        public void OnEnd_ShouldExportStopLog()
        {
            var activity = new Activity("TestActivity");

            _processor.OnStart(activity);

            _processor.OnEnd(activity);

            Assert.Contains(activity.SpanId, _processor.ActiveActivities);
            Assert.Contains(new KeyValuePair<ActivitySpanId, Activity>(activity.SpanId, activity),
                _processor.EndedActivities);
            Assert.Equal(2, exportedLogs.Count);
        }

        [Fact]
        public void OnEndAfterHeartbeat_ShouldCleanupActivity()
        {
            var activity = new Activity("TestActivity");

            _processor.OnStart(activity);

            _processor.OnEnd(activity);

            Thread.Sleep(HeartbeatIntervalMilliseconds + HeartbeatIntervalMilliseconds / 2);

            Assert.DoesNotContain(activity.SpanId, _processor.ActiveActivities);
            Assert.DoesNotContain(
                new KeyValuePair<ActivitySpanId, Activity>(activity.SpanId, activity),
                _processor.EndedActivities);
            Assert.Equal(2, exportedLogs.Count);
        }

        [Fact]
        public void Heartbeat_ShouldExportLogRecords()
        {
            var activity = new Activity("TestActivity");

            _processor.OnStart(activity);

            Assert.Single(exportedLogs);
            Thread.Sleep(HeartbeatIntervalMilliseconds + HeartbeatIntervalMilliseconds / 2);
            Assert.Equal(2, exportedLogs.Count);
            Thread.Sleep(HeartbeatIntervalMilliseconds);
            Assert.Equal(3, exportedLogs.Count);
        }
    }
}