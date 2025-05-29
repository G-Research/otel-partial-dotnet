using System.Diagnostics;
using System.Net;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace GR.OpenTelemetry.Processor.Partial.Tests;

public class PartialActivityProcessorTests : IDisposable
{
    private const int HeartbeatIntervalMilliseconds = 1000;
    private const int InitialHeartbeatDelayMilliseconds = 1000;
    private const int ProcessIntervalMilliseconds = 0;
    private readonly List<LogRecord> _exportedLogs = [];

    private readonly PartialActivityProcessor _processor;
    private readonly HttpListener _httpListener;
    private readonly List<string> _receivedRequests = [];
    private readonly string _mockOtlpEndpoint;

    public PartialActivityProcessorTests()
    {
        InMemoryExporter<LogRecord>
            logExporter = new InMemoryExporter<LogRecord>(_exportedLogs);
        _processor = new PartialActivityProcessor(logExporter, HeartbeatIntervalMilliseconds,
            InitialHeartbeatDelayMilliseconds, ProcessIntervalMilliseconds);

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
        _exportedLogs.Clear();
        StopMockOtlpEndpoint();
    }

    [Fact]
    public void Constructor_ShouldThrowExceptionForInvalidParameters()
    {
        var logExporter = new InMemoryExporter<LogRecord>(new List<LogRecord>());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PartialActivityProcessor(logExporter,
                heartbeatIntervalMilliseconds: -1)); // Invalid heartbeat interval

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PartialActivityProcessor(logExporter,
                initialHeartbeatDelayMilliseconds: -1)); // Invalid initial delay

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PartialActivityProcessor(logExporter,
                processIntervalMilliseconds: -1)); // Invalid process interval

#if NET
        Assert.Throws<ArgumentNullException>(() =>
            new PartialActivityProcessor(logExporter: null!)); // Null log exporter
#else
Assert.Throws<ArgumentOutOfRangeException>(() =>
    new PartialActivityProcessor(logExporter: null!)); // Null log exporter
#endif
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
        Assert.Single(_exportedLogs);
    }

    [Fact]
    public void OnEnd_ShouldExportStopLog()
    {
        var activity = new Activity("TestActivity");

        _processor.OnStart(activity);
        Assert.Contains(activity.SpanId, _processor.ActiveActivities);

        _processor.OnEnd(activity);
        Assert.DoesNotContain(activity.SpanId, _processor.ActiveActivities);
        Assert.True(_exportedLogs.Count >= 2);
    }

    [Fact]
    public void OnEndAfterHeartbeat_ShouldCleanupActivity()
    {
        var activity = new Activity("TestActivity");

        _processor.OnStart(activity);
        _processor.OnEnd(activity);

        Assert.Empty(_processor.ActiveActivities);
        Assert.True(_exportedLogs.Count >= 2);
    }

    [Fact]
    public void
        DelayedHeartbeatActivities_ShouldMoveActivitiesToReadyHeartbeatActivitiesAfterProcessing()
    {
        var activity = new Activity("TestActivity");
        var spanId = activity.SpanId;

        _processor.OnStart(activity);

        var delayedHeartbeatActivityRemoved = SpinWait.SpinUntil(
            () => _processor.DelayedHeartbeatActivities.All(valueTuple =>
                valueTuple.SpanId != spanId),
            TimeSpan.FromSeconds(10));
        Assert.True(delayedHeartbeatActivityRemoved,
            "Activity with delayed heartbeat was not removed in time.");

        var readyHeartbeatActivityRemoved = SpinWait.SpinUntil(
            () => _processor.ReadyHeartbeatActivities.Any(valueTuple =>
                valueTuple.SpanId == spanId),
            TimeSpan.FromSeconds(10));
        Assert.True(readyHeartbeatActivityRemoved,
            "Activity ready for heartbeat was not added in time.");

        var logCountMatch =
            SpinWait.SpinUntil(() => _exportedLogs.Count >= 2, TimeSpan.FromSeconds(10));
        Assert.True(logCountMatch, "Heartbeat log was not exported in time.");
    }

    [Fact]
    public void ReadyHeartbeatActivities_ShouldProcessHeartbeatLogsAfterProcessing()
    {
        var activity = new Activity("TestActivity");
        var spanId = activity.SpanId;

        _processor.OnStart(activity);

        var heartbeatReadyActivityAdded = SpinWait.SpinUntil(
            () => _processor.ReadyHeartbeatActivities.Any(valueTuple =>
                valueTuple.SpanId == spanId),
            TimeSpan.FromSeconds(10));
        Assert.True(heartbeatReadyActivityAdded,
            "Activity ready for heartbeat was not added in time.");

        // HACK: because of test flakyness in ci, this was added so that activity is no longer added to ready heartbeat activities because it is not active anymore
        _processor.OnEnd(activity);

        var heartbeatReadyActivityRemoved = SpinWait.SpinUntil(
            () => _processor.ReadyHeartbeatActivities.All(valueTuple =>
                valueTuple.SpanId != spanId),
            TimeSpan.FromSeconds(15));
        Assert.True(heartbeatReadyActivityRemoved,
            "Activity ready for heartbeat was not removed in time.");

        var logCountMatch =
            SpinWait.SpinUntil(() => _exportedLogs.Count >= 2, TimeSpan.FromSeconds(10));
        Assert.True(logCountMatch, "Heartbeat log was not exported in time.");
    }
}