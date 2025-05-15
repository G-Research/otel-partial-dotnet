using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace GR.OpenTelemetry.Processor.Partial;

public class PartialActivityProcessor : BaseProcessor<Activity>
{
    private const int DefaultHeartbeatIntervalMilliseconds = 5000;
    private int heartbeatIntervalMilliseconds;
    private Thread exporterThread;
    private ManualResetEvent shutdownTrigger;
    private readonly Lazy<ILogger<PartialActivityProcessor>> logger;
    private readonly Lazy<ILoggerFactory> loggerFactory;

    private ConcurrentDictionary<ActivitySpanId, Activity> activeActivities;
    private ConcurrentQueue<KeyValuePair<ActivitySpanId, Activity>> endedActivities;
    public IReadOnlyDictionary<ActivitySpanId, Activity> ActiveActivities => activeActivities;

    public IReadOnlyCollection<KeyValuePair<ActivitySpanId, Activity>> EndedActivities =>
        endedActivities;

    private readonly BaseExporter<LogRecord> logExporter;
    private readonly BaseProcessor<LogRecord> logProcessor;

    public PartialActivityProcessor(BaseExporter<LogRecord> logExporter,
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
        logProcessor = new SimpleLogRecordExportProcessor(logExporter);

        if (heartbeatIntervalMilliseconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatIntervalMilliseconds));
        }

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

        loggerFactory = new Lazy<ILoggerFactory>(CreateLoggerFactory);
        logger = new Lazy<ILogger<PartialActivityProcessor>>(InitializeLogger);
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

        using (logger.Value.BeginScope(GetHeartbeatLogRecordAttributes()))
        {
            foreach (var keyValuePair in activeActivities)
            {
                logger.Value.LogInformation(SpecHelper.Json(new TracesData(keyValuePair.Value,
                    TracesData.Signal.Heartbeat)));
            }
        }
    }

    public override void OnStart(Activity data)
    {
        // Access logger.Value to ensure lazy initialization
        using (logger.Value.BeginScope(GetHeartbeatLogRecordAttributes()))
        {
            logger.Value.LogInformation(
                SpecHelper.Json(new TracesData(data, TracesData.Signal.Heartbeat)));
        }

        activeActivities[data.SpanId] = data;
    }

    private ILogger<PartialActivityProcessor> InitializeLogger()
    {
        return loggerFactory.Value.CreateLogger<PartialActivityProcessor>();
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddOpenTelemetry(options =>
            {
                options.IncludeScopes = true;
                options.AddProcessor(logProcessor);
                options.SetResourceBuilder(ResourceBuilder.CreateEmpty()
                    .AddAttributes(ParentProvider.GetResource().Attributes));
            });
        });
    }

    public override void OnEnd(Activity data)
    {
        using (logger.Value.BeginScope(GetStopLogRecordAttributes()))
        {
            logger.Value.LogInformation(
                SpecHelper.Json(new TracesData(data, TracesData.Signal.Stop)));
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
                return logExporter.Shutdown() && logProcessor.Shutdown();
            case 0:
                return logExporter.Shutdown(0) && logProcessor.Shutdown(0);
        }

        var sw = Stopwatch.StartNew();
        exporterThread.Join(timeoutMilliseconds);
        var timeout = timeoutMilliseconds - sw.ElapsedMilliseconds;
        return logExporter.Shutdown((int)Math.Max(timeout, 0)) &&
               logProcessor.Shutdown((int)Math.Max(timeout, 0));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        shutdownTrigger.Dispose();
        logProcessor.Dispose();
        if (loggerFactory.IsValueCreated)
        {
            loggerFactory.Value.Dispose();
        }
    }

    private Dictionary<string, object> GetHeartbeatLogRecordAttributes() => new()
    {
        ["partial.event"] = "heartbeat",
        ["partial.frequency"] = $"{heartbeatIntervalMilliseconds}ms",
        ["partial.body.type"] = "json/v1",
    };

    private static Dictionary<string, object> GetStopLogRecordAttributes() => new()
    {
        ["partial.event"] = "stop",
        ["partial.body.type"] = "json/v1",
    };
}