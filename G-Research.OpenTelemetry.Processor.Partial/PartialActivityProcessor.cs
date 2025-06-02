using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace GR.OpenTelemetry.Processor.Partial;

public class PartialActivityProcessor : BaseProcessor<Activity>
{
    private const int DefaultHeartbeatIntervalMilliseconds = 5000;
    private const int DefaultInitialHeartbeatDelayMilliseconds = 5000;
    private const int DefaultProcessIntervalMilliseconds = 5000;

    private readonly int _heartbeatIntervalMilliseconds;
    private readonly int _initialHeartbeatDelayMilliseconds;
    private readonly int _processIntervalMilliseconds;

    private readonly object _lock = new();

    private readonly Dictionary<ActivitySpanId, Activity> _activeActivities;

    private readonly Queue<(ActivitySpanId SpanId, DateTime InitialHeartbeatTime)>
        _delayedHeartbeatActivities;

    private readonly HashSet<ActivitySpanId> _delayedHeartbeatActivitiesLookup;

    private readonly Queue<(ActivitySpanId SpanId, DateTime NextHeartbeatTime)>
        _readyHeartbeatActivities;

    // added for tests convenience
    public IReadOnlyDictionary<ActivitySpanId, Activity> ActiveActivities => _activeActivities;

    public IReadOnlyCollection<(ActivitySpanId SpanId, DateTime InitialHeartbeatTime)>
        DelayedHeartbeatActivities => _delayedHeartbeatActivities;

    public IReadOnlyCollection<ActivitySpanId> DelayedHeartbeatActivitiesLookup =>
        _delayedHeartbeatActivitiesLookup;

    public IReadOnlyCollection<(ActivitySpanId SpanId, DateTime NextHeartbeatTime)>
        ReadyHeartbeatActivities => _readyHeartbeatActivities;

    private readonly BaseExporter<LogRecord> _logExporter;
    private readonly BaseProcessor<LogRecord> _logProcessor;
    private readonly Lazy<ILogger<PartialActivityProcessor>> _logger;
    private readonly Lazy<ILoggerFactory> _loggerFactory;

    private readonly Thread _processorThread;
    private readonly ManualResetEvent _shutdownTrigger;

    public PartialActivityProcessor(
        BaseExporter<LogRecord> logExporter,
        int heartbeatIntervalMilliseconds = DefaultHeartbeatIntervalMilliseconds,
        int initialHeartbeatDelayMilliseconds = DefaultInitialHeartbeatDelayMilliseconds,
        int processIntervalMilliseconds = DefaultProcessIntervalMilliseconds
    )
    {
        ValidateParameters(logExporter, heartbeatIntervalMilliseconds,
            initialHeartbeatDelayMilliseconds,
            processIntervalMilliseconds);

        _logExporter = logExporter;
        _logProcessor = new SimpleLogRecordExportProcessor(logExporter);

        _heartbeatIntervalMilliseconds = heartbeatIntervalMilliseconds;
        _initialHeartbeatDelayMilliseconds = initialHeartbeatDelayMilliseconds;
        _processIntervalMilliseconds = processIntervalMilliseconds;

        _delayedHeartbeatActivities = new Queue<(ActivitySpanId, DateTime)>();
        _delayedHeartbeatActivitiesLookup = new HashSet<ActivitySpanId>();
        _readyHeartbeatActivities = new Queue<(ActivitySpanId, DateTime)>();
        _activeActivities = new Dictionary<ActivitySpanId, Activity>();

        _shutdownTrigger = new ManualResetEvent(false);

        _processorThread = new Thread(ProcessQueues)
        {
            IsBackground = true,
            Name = $"OpenTelemetry-{nameof(PartialActivityProcessor)}"
        };
        _processorThread.Start();

        _loggerFactory = new Lazy<ILoggerFactory>(CreateLoggerFactory);
        _logger = new Lazy<ILogger<PartialActivityProcessor>>(InitializeLogger);
    }

    public override void OnStart(Activity data)
    {
        lock (_lock)
        {
            _activeActivities[data.SpanId] = data;
            _delayedHeartbeatActivitiesLookup.Add(data.SpanId);
            _delayedHeartbeatActivities.Enqueue((data.SpanId,
                DateTime.UtcNow.AddMilliseconds(_initialHeartbeatDelayMilliseconds)));
        }
    }

    public override void OnEnd(Activity data)
    {
        bool isDelayedHeartbeatPending;
        lock (_lock)
        {
            _activeActivities.Remove(data.SpanId);

            isDelayedHeartbeatPending = _delayedHeartbeatActivitiesLookup.Remove(data.SpanId);
        }

        if (isDelayedHeartbeatPending)
        {
            return;
        }

        using (_logger.Value.BeginScope(GetStopLogRecordAttributes()))
        {
            _logger.Value.LogInformation(
                SpecHelper.Json(new TracesData(data, TracesData.Signal.Stop)));
        }
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        try
        {
            _shutdownTrigger.Set();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        switch (timeoutMilliseconds)
        {
            case Timeout.Infinite:
                _processorThread.Join();
                return _logExporter.Shutdown() && _logProcessor.Shutdown();
            case 0:
                return _logExporter.Shutdown(0) && _logProcessor.Shutdown(0);
        }

        var sw = Stopwatch.StartNew();
        _processorThread.Join(timeoutMilliseconds);
        var timeout = timeoutMilliseconds - sw.ElapsedMilliseconds;
        return _logExporter.Shutdown((int)Math.Max(timeout, 0)) &&
               _logProcessor.Shutdown((int)Math.Max(timeout, 0));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _shutdownTrigger.Dispose();
        _logProcessor.Dispose();
        if (_loggerFactory.IsValueCreated)
        {
            _loggerFactory.Value.Dispose();
        }
    }

    private ILogger<PartialActivityProcessor> InitializeLogger()
    {
        return _loggerFactory.Value.CreateLogger<PartialActivityProcessor>();
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddOpenTelemetry(options =>
            {
                options.IncludeScopes = true;
                options.AddProcessor(_logProcessor);
                options.SetResourceBuilder(ResourceBuilder.CreateEmpty()
                    .AddAttributes(ParentProvider.GetResource().Attributes));
            });
        });
    }

    private Dictionary<string, object> GetHeartbeatLogRecordAttributes() => new()
    {
        ["partial.event"] = "heartbeat",
        ["partial.frequency"] = $"{_heartbeatIntervalMilliseconds}ms",
        ["partial.body.type"] = "json/v1",
    };

    private static Dictionary<string, object> GetStopLogRecordAttributes() => new()
    {
        ["partial.event"] = "stop",
        ["partial.body.type"] = "json/v1",
    };

    private void ProcessQueues()
    {
        var triggers = new WaitHandle[] { _shutdownTrigger };

        while (true)
        {
            try
            {
                WaitHandle.WaitAny(triggers, _processIntervalMilliseconds);

                ProcessDelayedHeartbeatActivities();
                ProcessReadyHeartbeatActivities();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void ProcessDelayedHeartbeatActivities()
    {
        List<Activity> activitiesToBeLogged = [];
        lock (_lock)
        {
            while (true)
            {
                if (_delayedHeartbeatActivities.Count == 0)
                {
                    break;
                }

                var peekedItem = _delayedHeartbeatActivities.Peek();
                if (peekedItem.InitialHeartbeatTime > DateTime.UtcNow)
                {
                    break;
                }

                _delayedHeartbeatActivitiesLookup.Remove(peekedItem.SpanId);
                _delayedHeartbeatActivities.Dequeue();

                if (!_activeActivities.TryGetValue(peekedItem.SpanId, out var activity))
                {
                    continue;
                }

                activitiesToBeLogged.Add(activity);

                _readyHeartbeatActivities.Enqueue((peekedItem.SpanId,
                    DateTime.UtcNow.AddMilliseconds(_heartbeatIntervalMilliseconds)));
            }
        }

        LogActivities(activitiesToBeLogged);
    }

    private void ProcessReadyHeartbeatActivities()
    {
        List<Activity> activitiesToBeLogged = [];
        lock (_lock)
        {
            while (true)
            {
                if (_readyHeartbeatActivities.Count == 0)
                {
                    break;
                }

                var peekedItem = _readyHeartbeatActivities.Peek();
                if (peekedItem.NextHeartbeatTime > DateTime.UtcNow)
                {
                    break;
                }

                _readyHeartbeatActivities.Dequeue();

                if (!_activeActivities.TryGetValue(peekedItem.SpanId, out var activity))
                {
                    continue;
                }

                activitiesToBeLogged.Add(activity);

                _readyHeartbeatActivities.Enqueue((peekedItem.SpanId,
                    DateTime.UtcNow.AddMilliseconds(_heartbeatIntervalMilliseconds)));
            }
        }

        LogActivities(activitiesToBeLogged);
    }

    private void LogActivities(List<Activity> activitiesToBeLogged)
    {
        foreach (var activity in activitiesToBeLogged)
            // begin scope needs to happen inside foreach so resource is properly set
            using (_logger.Value.BeginScope(GetHeartbeatLogRecordAttributes()))
            {
                {
                    _logger.Value.LogInformation(
                        SpecHelper.Json(new TracesData(activity, TracesData.Signal.Heartbeat)));
                }
            }
    }

    private static void ValidateParameters(BaseExporter<LogRecord> logExporter,
        int heartbeatIntervalMilliseconds,
        int initialHeartbeatDelayMilliseconds, int processIntervalMilliseconds)
    {
#if NET
        ArgumentNullException.ThrowIfNull(logExporter);
#else
        if (logExporter == null)
        {
            throw new ArgumentOutOfRangeException(nameof(logExporter));
        }
#endif

        if (heartbeatIntervalMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatIntervalMilliseconds),
                "Heartbeat interval must be greater than zero.");
        }

        if (initialHeartbeatDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialHeartbeatDelayMilliseconds),
                "Initial heartbeat delay must be zero or greater.");
        }

        if (processIntervalMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processIntervalMilliseconds),
                "Process interval must be zero or greater.");
        }
    }
}