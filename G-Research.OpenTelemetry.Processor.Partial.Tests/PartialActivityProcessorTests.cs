using System.Diagnostics;
using System.Net;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace GR.OpenTelemetry.Processor.Partial.Tests
{
    public class PartialActivityProcessorTests : IDisposable
    {
        private const int HeartbeatIntervalMilliseconds = 1000;
        private readonly List<LogRecord> exportedLogs = new();
        private readonly InMemoryExporter<LogRecord> logExporter;
        private readonly PartialActivityProcessor _processor;
        private readonly HttpListener _httpListener;
        private readonly List<string> _receivedRequests = new();
        private readonly string _mockOtlpEndpoint;

        public PartialActivityProcessorTests()
        {
            logExporter = new InMemoryExporter<LogRecord>(exportedLogs);
            _processor = new PartialActivityProcessor(logExporter, 1000);

            var randomPort = new Random().Next(40000, 50000);
            _mockOtlpEndpoint = $"http://localhost:{randomPort}";

            _httpListener = new HttpListener();
            StartMockOtlpEndpoint();
        }

        private void StartMockOtlpEndpoint()
        {
            _httpListener.Prefixes.Add(_mockOtlpEndpoint + "/");
            _httpListener.Start();

            Task.Run(() =>
            {
                while (_httpListener.IsListening)
                {
                    try
                    {
                        var context = _httpListener.GetContext();
                        using var reader =
                            new StreamReader(context.Request.InputStream, Encoding.UTF8);
                        var requestBody = reader.ReadToEnd();
                        _receivedRequests.Add(requestBody);

                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        context.Response.Close();
                    }
                    catch (Exception)
                    {
                        // Ignore exceptions when stopping the listener
                    }
                }
            });
        }

        private void StopMockOtlpEndpoint()
        {
            if (_httpListener is { IsListening: true })
            {
                _httpListener.Stop();
                _httpListener.Close();
            }
        }

        public void Dispose()
        {
            StopMockOtlpEndpoint();
        }

        [Fact]
        public void Log_ShouldContainDefinedResource()
        {
            ActivitySource activitySource = new("activitySourceTest");
            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = _ => true,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                    ActivitySamplingResult.AllData,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllData,
            });

            var otlpLogExporter = new OtlpLogExporter(new OtlpExporterOptions
            {
                Protocol = OtlpExportProtocol.HttpProtobuf,
                Endpoint = new Uri(_mockOtlpEndpoint)
            });

            Sdk.CreateTracerProviderBuilder()
                .AddSource("activitySourceTest")
                .ConfigureResource(configure => { configure.AddService("TestService"); })
                .AddProcessor(new PartialActivityProcessor(otlpLogExporter, 1000))
                .Build();

            var activity = activitySource.CreateActivity("activityTest", ActivityKind.Internal);
            activity?.Start();
            activity?.Stop();

            Assert.NotEmpty(_receivedRequests);
            var firstRequest = _receivedRequests.First();
            Assert.Contains("TestService", firstRequest);
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