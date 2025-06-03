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
                heartbeatIntervalMilliseconds: 1000, initialHeartbeatDelayMilliseconds: 6000,
                processIntervalMilliseconds: 1000))
            .AddProcessor(new SimpleActivityExportProcessor(otlpExporter))
            .Build();


        using (activitySource.StartActivity("activity 1"))
        {
            using (activitySource.StartActivity("activity 2"))
            {
                Console.WriteLine("sleeping inside activity 2");
                Thread.Sleep(2000);
            }
            Console.WriteLine("sleeping inside activity 1");
            Thread.Sleep(5000);
        }

        Console.WriteLine("sleeping outside activities");
        Thread.Sleep(5000);
    }
}