using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace GR.OpenTelemetry.Processor.Partial;

public class PartialActivityProcessor : BaseProcessor<Activity>
{
    private const int DefaultHeartbeatIntervalMilliseconds = 5000;
    private int heartbeatIntervalMilliseconds;
    private Thread exporterThread;
    private ManualResetEvent shutdownTrigger;

    private ConcurrentDictionary<ActivitySpanId, Activity> activeActivities;
    private ConcurrentQueue<KeyValuePair<ActivitySpanId, Activity>> endedActivities;
    public IReadOnlyDictionary<ActivitySpanId, Activity> ActiveActivities => activeActivities;

    public IReadOnlyCollection<KeyValuePair<ActivitySpanId, Activity>> EndedActivities =>
        endedActivities;

    private BaseExporter<LogRecord> logExporter;

    private static MethodInfo WriteTraceDataMethod;
    private static ConstructorInfo LogRecordConstructor;
    private static object SdkLimitOptions;

    public PartialActivityProcessor(
        BaseExporter<LogRecord> logExporter,
        int heartbeatIntervalMilliseconds = DefaultHeartbeatIntervalMilliseconds)
    {
#if NET
        ArgumentNullException.ThrowIfNull(logExporter);
#else
        if (logExporter == null)
        {
            throw new ArgumentOutOfRangeException(nameof(logExporter));
        }
#endif
        this.logExporter = logExporter;

#if NET
        ArgumentOutOfRangeException.ThrowIfLessThan(heartbeatIntervalMilliseconds, 1);
#else
        if (heartbeatIntervalMilliseconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatIntervalMilliseconds));
        }
#endif
        this.heartbeatIntervalMilliseconds = heartbeatIntervalMilliseconds;

        activeActivities = new ConcurrentDictionary<ActivitySpanId, Activity>();
        endedActivities = new ConcurrentQueue<KeyValuePair<ActivitySpanId, Activity>>();

        shutdownTrigger = new ManualResetEvent(false);

        // Access OpenTelemetry internals as soon as possible to fail fast rather than waiting for the first heartbeat
        AccessOpenTelemetryInternals(out WriteTraceDataMethod, out LogRecordConstructor,
            out SdkLimitOptions);

        exporterThread = new Thread(ExporterProc)
        {
            IsBackground = true,
            Name = $"OpenTelemetry-{nameof(PartialActivityProcessor)}",
        };
        exporterThread.Start();
    }

    private void ExporterProc()
    {
        var triggers = new WaitHandle[] { shutdownTrigger };

        while (true)
        {
            try
            {
                WaitHandle.WaitAny(triggers, heartbeatIntervalMilliseconds);
                Heartbeat();
            }
            catch (ObjectDisposedException)
            {
                // the exporter is somehow disposed before the worker thread could finish its job
                return;
            }
        }
    }

    private void Heartbeat()
    {
        // remove ended activities from active activities
        while (endedActivities.TryDequeue(out var activity))
        {
            activeActivities.TryRemove(activity.Key, out _);
        }

        foreach (var keyValuePair in activeActivities)
        {
            var logRecord = GetLogRecord(keyValuePair.Value, GetHeartbeatLogRecordAttributes());
            logExporter.Export(new Batch<LogRecord>(logRecord));
        }
    }

    public override void OnStart(Activity data)
    {
        var logRecord = GetLogRecord(data, GetHeartbeatLogRecordAttributes());
        logExporter.Export(new Batch<LogRecord>(logRecord));
        activeActivities[data.SpanId] = data;
    }

    private static LogRecord GetLogRecord(
        Activity data,
        List<KeyValuePair<string, object?>> logRecordAttributesToBeAdded)
    {
        var buffer = new byte[750000];

        var result = WriteTraceDataMethod.Invoke(
            null,
            [buffer, 0, SdkLimitOptions, null!, new Batch<Activity>(data)]);
        var writePosition = result as int? ?? 0; // Use a default value if null

        var logRecord = (LogRecord)LogRecordConstructor.Invoke(null);
        logRecord.Timestamp = DateTime.UtcNow;
        logRecord.TraceId = data.TraceId;
        logRecord.SpanId = data.SpanId;
        logRecord.TraceFlags = ActivityTraceFlags.None;
        logRecord.Body = Convert.ToBase64String(buffer, 0, writePosition);

        // Severity = LogRecordSeverity.Info,
        // SeverityText = "Info",

        var logRecordAttributes = GetLogRecordAttributes();
        logRecordAttributes.AddRange(logRecordAttributesToBeAdded);
        logRecord.Attributes = logRecordAttributes;

        return logRecord;
    }

    private static void AccessOpenTelemetryInternals(out MethodInfo writeTraceDataMethod,
        out ConstructorInfo logRecordConstructor, out object sdkLimitOptions)
    {
        var sdkLimitOptionsType = Type.GetType(
            "OpenTelemetry.Exporter.OpenTelemetryProtocol.Implementation.SdkLimitOptions, OpenTelemetry.Exporter.OpenTelemetryProtocol",
            true);

        if (sdkLimitOptionsType == null)
        {
            throw new InvalidOperationException("Failed to get the type 'SdkLimitOptions'.");
        }

        sdkLimitOptions = Activator.CreateInstance(sdkLimitOptionsType, nonPublic: true) ??
                          throw new InvalidOperationException(
                              "Failed to create an instance of 'SdkLimitOptions'.");

        var protobufOtlpTraceSerializerType = Type.GetType(
            "OpenTelemetry.Exporter.OpenTelemetryProtocol.Implementation.Serializer.ProtobufOtlpTraceSerializer, OpenTelemetry.Exporter.OpenTelemetryProtocol",
            true);

        if (protobufOtlpTraceSerializerType == null)
        {
            throw new InvalidOperationException(
                "Failed to get the type 'ProtobufOtlpTraceSerializer'.");
        }

        writeTraceDataMethod =
            protobufOtlpTraceSerializerType.GetMethod("WriteTraceData",
                BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new InvalidOperationException("Failed to get the method 'WriteTraceData'.");

        if (writeTraceDataMethod == null)
        {
            throw new InvalidOperationException("Failed to get the method 'WriteTraceData'.");
        }

        var logRecordType = Type.GetType("OpenTelemetry.Logs.LogRecord, OpenTelemetry", true);

        if (logRecordType == null)
        {
            throw new InvalidOperationException("Failed to get the type 'LogRecord'.");
        }

        logRecordConstructor = logRecordType.GetConstructor(
                                   BindingFlags.NonPublic | BindingFlags.Instance, null,
                                   Type.EmptyTypes, null) ??
                               throw new InvalidOperationException(
                                   "Failed to get the constructor of 'LogRecord'.");
    }

    public override void OnEnd(Activity data)
    {
        var logRecord = GetLogRecord(data, GetStopLogRecordAttributes());
        logExporter.Export(new Batch<LogRecord>(logRecord));
        endedActivities.Enqueue(new KeyValuePair<ActivitySpanId, Activity>(data.SpanId, data));
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        try
        {
            shutdownTrigger.Set();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        switch (timeoutMilliseconds)
        {
            case Timeout.Infinite:
                exporterThread.Join();
                return logExporter.Shutdown();
            case 0:
                return logExporter.Shutdown(0);
        }

        var sw = Stopwatch.StartNew();
        exporterThread.Join(timeoutMilliseconds);
        var timeout = timeoutMilliseconds - sw.ElapsedMilliseconds;
        return logExporter.Shutdown((int)Math.Max(timeout, 0));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        shutdownTrigger.Dispose();
    }

    private static List<KeyValuePair<string, object?>> GetLogRecordAttributes() =>
    [
        new("telemetry.logs.cluster", "partial"),
        new("telemetry.logs.project", "span"),
    ];

    private List<KeyValuePair<string, object?>> GetHeartbeatLogRecordAttributes() =>
    [
        new("partial.event", "heartbeat"),
        new("partial.frequency", heartbeatIntervalMilliseconds + "ms")
    ];

    private static List<KeyValuePair<string, object?>> GetStopLogRecordAttributes() =>
    [
        new("partial.event", "stop"),
    ];
}