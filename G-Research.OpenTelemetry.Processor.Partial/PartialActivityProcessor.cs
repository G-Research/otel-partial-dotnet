using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace GR.OpenTelemetry.Processor.Partial;

public class PartialActivityProcessor : BaseProcessor<Activity>
{
    private const int DefaultHeartbeatIntervalMilliseconds = 5000;
    private int heartbeatIntervalMilliseconds;
    private Thread exporterThread;
    private ManualResetEvent shutdownTrigger;
    private readonly ILogger<PartialActivityProcessor> logger;

    private ConcurrentDictionary<ActivitySpanId, Activity> activeActivities;
    private ConcurrentQueue<KeyValuePair<ActivitySpanId, Activity>> endedActivities;
    public IReadOnlyDictionary<ActivitySpanId, Activity> ActiveActivities => activeActivities;

    public IReadOnlyCollection<KeyValuePair<ActivitySpanId, Activity>> EndedActivities =>
        endedActivities;

    private BaseExporter<LogRecord> logExporter;

    public PartialActivityProcessor(
        BaseExporter<LogRecord> logExporter,
        int heartbeatIntervalMilliseconds = DefaultHeartbeatIntervalMilliseconds)
    {
        // Configure OpenTelemetry logging to use the provided logExporter
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddOpenTelemetry(options =>
            {
                // TODO should SimpleLogRecordExportProcessor be shut down?
                options.IncludeScopes = true;
                options.AddProcessor(new SimpleLogRecordExportProcessor(logExporter));
            });
        });

        logger = loggerFactory.CreateLogger<PartialActivityProcessor>();

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
            using (logger.BeginScope(GetHeartbeatLogRecordAttributes()))
            {
                // TODO use keyValuePair here
                logger.LogInformation("log body");
            }
        }
    }

    public override void OnStart(Activity data)
    {
        using (logger.BeginScope(GetHeartbeatLogRecordAttributes()))
        {
            // TODO use data here
            logger.LogInformation("log body");
        }

        activeActivities[data.SpanId] = data;
    }

    /*
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
    */

    public override void OnEnd(Activity data)
    {
        using (logger.BeginScope(GetStopLogRecordAttributes()))
        {
            // TODO use data here
            logger.LogInformation("log body");
        }

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

    private Dictionary<string, object> GetHeartbeatLogRecordAttributes() => new()
    {
        ["partial.event"] = "heartbeat",
        ["partial.frequency"] = $"{heartbeatIntervalMilliseconds}ms",
        ["telemetry.logs.cluster"] = "partial",
        ["telemetry.logs.project"] = "span"
    };

    private static Dictionary<string, object> GetStopLogRecordAttributes() => new()
    {
        ["partial.event"] = "stop",
        ["telemetry.logs.cluster"] = "partial",
        ["telemetry.logs.project"] = "span"
    };
}