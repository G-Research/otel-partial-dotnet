using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

public class CapturingLogExporter(Resource resource) : BaseExporter<LogRecord>
{
    public readonly List<(LogRecord Log, Resource Resource)> Exported = [];

    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        foreach (var log in batch)
        {
            Exported.Add((log, resource));
        }

        return ExportResult.Success;
    }
}