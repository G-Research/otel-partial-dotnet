using System.Diagnostics;
using GR.OpenTelemetry.Processor.Partial;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using Xunit;

namespace GR.Processor.Partial.Tests
{
    public class PartialActivityProcessorTests
    {
        private List<LogRecord> exportedLogs = [];
        private InMemoryExporter<LogRecord> logExporter;
        private PartialActivityProcessor processor;
        private const int heartbeatInervalMs = 1000;

        public PartialActivityProcessorTests()
        {
            logExporter = new InMemoryExporter<LogRecord>(exportedLogs);
            processor =
                new PartialActivityProcessor(logExporter, heartbeatInervalMs);
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

            Thread.Sleep(heartbeatInervalMs);

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
            Thread.Sleep(heartbeatInervalMs + 100);
            Assert.Equal(2, exportedLogs.Count);
            Thread.Sleep(heartbeatInervalMs + 100);
            Assert.Equal(3, exportedLogs.Count);
        }
    }
}