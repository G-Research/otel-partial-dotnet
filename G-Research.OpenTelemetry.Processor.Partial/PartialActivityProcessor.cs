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
    private const int DefaultInitialHeartbeatDelayMilliseconds = 5000;
    private const int DefaultProcessIntervalMilliseconds = 5000;

    private readonly int _heartbeatIntervalMilliseconds;
    private readonly int _initialHeartbeatDelayMilliseconds;
    private readonly int _processIntervalMilliseconds;

    private readonly ConcurrentDictionary<ActivitySpanId, Activity> _activeActivities;

    private readonly ConcurrentQueue<(ActivitySpanId SpanId, DateTime InitialHeartbeatTime)>
        _startedSpansQueue;

    private readonly ConcurrentQueue<(ActivitySpanId SpanId, DateTime NextHeartbeatTime)>
        _heartbeatSpansQueue;

    // added for tests convenience
    public IReadOnlyDictionary<ActivitySpanId, Activity> ActiveActivities => _activeActivities;

    public IReadOnlyList<(ActivitySpanId SpanId, DateTime InitialHeartbeatTime)>
        StartedSpansQueue =>
        _startedSpansQueue.ToList();

    public IReadOnlyList<(ActivitySpanId SpanId, DateTime NextHeartbeatTime)> HeartbeatSpansQueue =>
        _heartbeatSpansQueue.ToList();

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

        _startedSpansQueue = new ConcurrentQueue<(ActivitySpanId, DateTime)>();
        _heartbeatSpansQueue = new ConcurrentQueue<(ActivitySpanId, DateTime)>();
        _activeActivities = new ConcurrentDictionary<ActivitySpanId, Activity>();

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
        using (_logger.Value.BeginScope(GetHeartbeatLogRecordAttributes()))
        {
            _logger.Value.LogInformation(
                SpecHelper.Json(new TracesData(data, TracesData.Signal.Heartbeat)));
        }

        _activeActivities[data.SpanId] = data;
        _startedSpansQueue.Enqueue((data.SpanId,
            DateTime.UtcNow.AddMilliseconds(_initialHeartbeatDelayMilliseconds)));
    }

    public override void OnEnd(Activity data)
    {
        using (_logger.Value.BeginScope(GetStopLogRecordAttributes()))
        {
            _logger.Value.LogInformation(
                SpecHelper.Json(new TracesData(data, TracesData.Signal.Stop)));
        }

        _activeActivities.TryRemove(data.SpanId, out _);
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

                ProcessStartedSpansQueue();
                ProcessHeartbeatSpansQueue();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void ProcessStartedSpansQueue()
    {
        while (_startedSpansQueue.TryPeek(out var span) &&
               span.InitialHeartbeatTime <= DateTime.UtcNow)
        {
            _startedSpansQueue.TryDequeue(out span);

            if (_activeActivities.TryGetValue(span.SpanId, out _))
            {
                _heartbeatSpansQueue.Enqueue((span.SpanId,
                    DateTime.UtcNow.AddMilliseconds(_heartbeatIntervalMilliseconds)));
            }
        }
    }

    private void ProcessHeartbeatSpansQueue()
    {
        while (_heartbeatSpansQueue.TryPeek(out var span) &&
               span.NextHeartbeatTime <= DateTime.UtcNow)
        {
            _heartbeatSpansQueue.TryDequeue(out span);

            if (!_activeActivities.TryGetValue(span.SpanId, out var activity))
            {
                continue;
            }

            using (_logger.Value.BeginScope(GetHeartbeatLogRecordAttributes()))
            {
                _logger.Value.LogInformation(
                    SpecHelper.Json(new TracesData(activity, TracesData.Signal.Heartbeat)));
            }

            _heartbeatSpansQueue.Enqueue((span.SpanId,
                DateTime.UtcNow.AddMilliseconds(_heartbeatIntervalMilliseconds)));
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