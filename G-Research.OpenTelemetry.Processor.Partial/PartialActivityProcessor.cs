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
    private const int DefaultHeartbeatDelayMilliseconds = 5000;
    private readonly int heartbeatIntervalMilliseconds;
    private readonly int heartbeatDelayMilliseconds;
    private readonly Thread exporterThread;
    private readonly ManualResetEvent shutdownTrigger;
    private readonly Lazy<ILogger<PartialActivityProcessor>> logger;
    private readonly Lazy<ILoggerFactory> loggerFactory;

    private readonly ConcurrentDictionary<ActivitySpanId, Activity> activeActivities;
    private readonly ConcurrentDictionary<ActivitySpanId, Activity> activitiesWithoutFirstHeartbeat;

    private readonly BaseExporter<LogRecord> logExporter;
    private readonly BaseProcessor<LogRecord> logProcessor;

    public IReadOnlyDictionary<ActivitySpanId, Activity> ActiveActivities => activeActivities;

    public IReadOnlyDictionary<ActivitySpanId, Activity> ActivitiesWithoutFirstHeartbeat =>
        activitiesWithoutFirstHeartbeat;


    public PartialActivityProcessor(BaseExporter<LogRecord> logExporter,
        int heartbeatIntervalMilliseconds = DefaultHeartbeatIntervalMilliseconds,
        int heartbeatDelayMilliseconds = DefaultHeartbeatDelayMilliseconds)
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

        if (heartbeatDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatDelayMilliseconds));
        }

        this.heartbeatDelayMilliseconds = heartbeatDelayMilliseconds;

        activeActivities = new ConcurrentDictionary<ActivitySpanId, Activity>();
        activitiesWithoutFirstHeartbeat = new ConcurrentDictionary<ActivitySpanId, Activity>();

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
                HeartbeatAsync().Wait();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private async Task HeartbeatAsync()
    {
        using (logger.Value.BeginScope(GetHeartbeatLogRecordAttributes()))
        {
            var keysSnapshot = activeActivities.Keys.ToList();
            var tasks = keysSnapshot.Select(async spanId =>
            {
                // handle the first heartbeat delay
                if (activitiesWithoutFirstHeartbeat.TryRemove(spanId, out _))
                {
                    await Task.Delay(heartbeatDelayMilliseconds);
                }

                if (activeActivities.TryGetValue(spanId, out var activity))
                {
                    logger.Value.LogInformation(
                        SpecHelper.Json(new TracesData(activity, TracesData.Signal.Heartbeat)));
                }
            });

            await Task.WhenAll(tasks);
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
        activitiesWithoutFirstHeartbeat[data.SpanId] = data;
    }

    public override void OnEnd(Activity data)
    {
        using (logger.Value.BeginScope(GetStopLogRecordAttributes()))
        {
            logger.Value.LogInformation(
                SpecHelper.Json(new TracesData(data, TracesData.Signal.Stop)));
        }

        activeActivities.TryRemove(data.SpanId, out _);
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