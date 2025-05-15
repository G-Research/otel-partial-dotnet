using System.Diagnostics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Xunit;

namespace GR.OpenTelemetry.Processor.Partial.Tests
{
    public class PartialActivityProcessorTests
    {
        private List<LogRecord> exportedLogs = [];
        private InMemoryExporter<LogRecord> logExporter;
        private PartialActivityProcessor processor;
        private const int HeartbeatIntervalMilliseconds = 1000;

        public PartialActivityProcessorTests()
        {
            logExporter = new InMemoryExporter<LogRecord>(exportedLogs);
            processor =
                new PartialActivityProcessor(logExporter, null, HeartbeatIntervalMilliseconds);
        }

        [Fact]
        public void Log_ShouldContainDefinedResource()
        {
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddAttributes(new Dictionary<string, object>
                {
                    { "service.name", "service-name-example" },
                });

            var resource = resourceBuilder.Build();

            var exporter = new CapturingLogExporter(resource);
            var processor = new PartialActivityProcessor(exporter, resourceBuilder, 1000);

            var activity = new Activity("TestActivity");
            activity.Start();
            processor.OnStart(activity);
            processor.OnEnd(activity);

            Thread.Sleep(100);

            Assert.NotEmpty(exporter.Exported);
            var first = exporter.Exported.First();
            Assert.Contains(first.Resource.Attributes,
                kvp => kvp.Key == "service.name" && kvp.Value.Equals("service-name-example"));
        }

        [Fact]
        public void Constructor_ShouldInitializeFields()
        {
            Assert.NotNull(processor);
        }

        [Fact]
        public void OnStart_ShouldExportHeartbeatLog()
        {
            var activity = new Activity("TestActivity");

            processor.OnStart(activity);

            Assert.Contains(activity.SpanId, processor.ActiveActivities);
            Assert.Single(exportedLogs);
        }

        [Fact]
        public void OnEnd_ShouldExportStopLog()
        {
            var activity = new Activity("TestActivity");

            processor.OnStart(activity);

            processor.OnEnd(activity);

            Assert.Contains(activity.SpanId, processor.ActiveActivities);
            Assert.Contains(new KeyValuePair<ActivitySpanId, Activity>(activity.SpanId, activity),
                processor.EndedActivities);
            Assert.Equal(2, exportedLogs.Count);
        }

        [Fact]
        public void OnEndAfterHeartbeat_ShouldCleanupActivity()
        {
            var activity = new Activity("TestActivity");

            processor.OnStart(activity);

            processor.OnEnd(activity);

            Thread.Sleep(HeartbeatIntervalMilliseconds + HeartbeatIntervalMilliseconds / 2);

            Assert.DoesNotContain(activity.SpanId, processor.ActiveActivities);
            Assert.DoesNotContain(
                new KeyValuePair<ActivitySpanId, Activity>(activity.SpanId, activity),
                processor.EndedActivities);
            Assert.Equal(2, exportedLogs.Count);
        }

        [Fact]
        public void Heartbeat_ShouldExportLogRecords()
        {
            var activity = new Activity("TestActivity");

            processor.OnStart(activity);

            Assert.Single(exportedLogs);
            Thread.Sleep(HeartbeatIntervalMilliseconds + HeartbeatIntervalMilliseconds / 2);
            Assert.Equal(2, exportedLogs.Count);
            Thread.Sleep(HeartbeatIntervalMilliseconds);
            Assert.Equal(3, exportedLogs.Count);
        }
    }
}