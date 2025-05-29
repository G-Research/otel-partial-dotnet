using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GR.OpenTelemetry.Processor.Partial;

public class Example
{
    public static void Main()
    {
        ActivitySource activitySource = new("activitySource");
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => true,
            SampleUsingParentId = (ref ActivityCreationOptions<string> options) =>
                ActivitySamplingResult.AllDataAndRecorded,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                ActivitySamplingResult.AllDataAndRecorded,
        });


        var otlpExporter = new OtlpTraceExporter(new OtlpExporterOptions
        {
            Protocol = OtlpExportProtocol.Grpc,
            Endpoint =
                new Uri("http://localhost:4317")
        });

        var otlpLogExporter = new OtlpLogExporter(new OtlpExporterOptions
        {
            Protocol = OtlpExportProtocol.HttpProtobuf,
            Endpoint = new Uri("http://localhost:4318/v1/logs")
        });

        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("activitySource")
            .ConfigureResource(configure => { configure.AddService("Example"); })
            .AddProcessor(new PartialActivityProcessor(logExporter: otlpLogExporter,
                heartbeatIntervalMilliseconds: 1000, heartbeatDelayMilliseconds: 6000))
            .AddProcessor(new SimpleActivityExportProcessor(otlpExporter))
            .Build();


        using (var activity1 = activitySource.StartActivity("activity"))
        {
            activity1?.SetTag("tag", "activity");
            Console.WriteLine("sleeping inside activity");
            Thread.Sleep(5000);
        }

        Console.WriteLine("sleeping outside activity");
        Thread.Sleep(5000);
    }
}