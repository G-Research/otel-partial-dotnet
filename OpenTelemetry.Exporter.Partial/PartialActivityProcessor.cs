using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry.Logs;

namespace OpenTelemetry.Exporter.Partial;

public class PartialActivityProcessor : BaseProcessor<Activity>
{
    private const int DefaultScheduledDelayMilliseconds = 5000;
    private int scheduledDelayMilliseconds;
    private Thread exporterThread;
    private AutoResetEvent exportTrigger;
    private ManualResetEvent shutdownTrigger;

    private ConcurrentDictionary<ActivitySpanId, Activity> activeActivities;
    private ConcurrentQueue<KeyValuePair<ActivitySpanId, Activity>> endedActivities;
    private BaseExporter<LogRecord> logExporter;

    public PartialActivityProcessor(
        BaseExporter<LogRecord> logExporter,
        int scheduledDelayMilliseconds = DefaultScheduledDelayMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(logExporter);
        this.logExporter = logExporter;

        ArgumentOutOfRangeException.ThrowIfLessThan(scheduledDelayMilliseconds, 1);
        this.scheduledDelayMilliseconds = scheduledDelayMilliseconds;

        activeActivities = new ConcurrentDictionary<ActivitySpanId, Activity>();
        endedActivities = new ConcurrentQueue<KeyValuePair<ActivitySpanId, Activity>>();

        exportTrigger = new AutoResetEvent(false);
        shutdownTrigger = new ManualResetEvent(false);

        exporterThread = new Thread(ExporterProc)
        {
            IsBackground = true,
            Name = $"OpenTelemetry-{nameof(PartialActivityProcessor)}",
        };
        exporterThread.Start();
    }

    private void ExporterProc()
    {
        var triggers = new WaitHandle[] { exportTrigger, shutdownTrigger };

        while (true)
        {
            try
            {
                WaitHandle.WaitAny(triggers, scheduledDelayMilliseconds);
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
        var sdkLimitOptionsType = Type.GetType(
            "OpenTelemetry.Exporter.OpenTelemetryProtocol.Implementation.SdkLimitOptions, OpenTelemetry.Exporter.OpenTelemetryProtocol",
            true);

        if (sdkLimitOptionsType == null)
        {
            throw new InvalidOperationException("Failed to get the type 'SdkLimitOptions'.");
        }

        var sdkLimitOptions = Activator.CreateInstance(sdkLimitOptionsType, nonPublic: true);

        if (sdkLimitOptions == null)
        {
            throw new InvalidOperationException(
                "Failed to create an instance of 'SdkLimitOptions'.");
        }

        var protobufOtlpTraceSerializerType = Type.GetType(
            "OpenTelemetry.Exporter.OpenTelemetryProtocol.Implementation.Serializer.ProtobufOtlpTraceSerializer, OpenTelemetry.Exporter.OpenTelemetryProtocol",
            true);

        if (protobufOtlpTraceSerializerType == null)
        {
            throw new InvalidOperationException(
                "Failed to get the type 'ProtobufOtlpTraceSerializer'.");
        }

        var writeTraceDataMethod = protobufOtlpTraceSerializerType.GetMethod(
            "WriteTraceData",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (writeTraceDataMethod == null)
        {
            throw new InvalidOperationException("Failed to get the method 'WriteTraceData'.");
        }

        var result = writeTraceDataMethod.Invoke(
            null,
            [buffer, 0, sdkLimitOptions, null!, new Batch<Activity>(data)]);
        var writePosition = result as int? ?? 0; // Use a default value if null

        var logRecordType = Type.GetType("OpenTelemetry.Logs.LogRecord, OpenTelemetry", true);

        if (logRecordType == null)
        {
            throw new InvalidOperationException("Failed to get the type 'LogRecord'.");
        }

        var logRecordConstructor = logRecordType.GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);

        if (logRecordConstructor == null)
        {
            throw new InvalidOperationException("Failed to get the constructor of 'LogRecord'.");
        }

        var logRecord = (LogRecord)logRecordConstructor.Invoke(null);
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

    public override void OnEnd(Activity data)
    {
        var logRecord = GetLogRecord(data, GetStopLogRecordAttributes());
        logExporter.Export(new Batch<LogRecord>(logRecord));
        endedActivities.Enqueue(new KeyValuePair<ActivitySpanId, Activity>(data.SpanId, data));
    }


    // TODO: export logs for all active activities
    protected override bool OnForceFlush(int timeoutMilliseconds)
    {
        return base.OnForceFlush(timeoutMilliseconds);
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
        exportTrigger.Dispose();
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
        new("partial.frequency", scheduledDelayMilliseconds + "ms")
    ];

    private static List<KeyValuePair<string, object?>> GetStopLogRecordAttributes() =>
    [
        new("partial.event", "stop"),
    ];
}