using System.Diagnostics;
using OpenTelemetry.Trace;

namespace OpenTelemetry.Exporter.Partial;

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
                new Uri("http://otel-partial-collector:4317")
        });

        var otlpLogExporter = new OtlpLogExporter(new OtlpExporterOptions
        {
            Protocol = OtlpExportProtocol.HttpProtobuf,
            Endpoint = new Uri("http://otel-partial-collector:4318/v1/logs")
        });

        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("activitySource")
            .AddProcessor(new PartialActivityProcessor(otlpLogExporter,
                logEmitInterval: 1000))
            .AddProcessor(new SimpleActivityExportProcessor(otlpExporter))
            .Build();


        using (var activity1 = activitySource.StartActivity("activity1"))
        {
            activity1?.SetTag("tag", "activity1");
            using (var activity2 = activitySource.StartActivity("activity2"))
            {
                activity2?.SetTag("tag", "activity2");
                activity2?.SetStatus(ActivityStatusCode.Ok);
                Console.WriteLine("sleeping inside activity2");
                Thread.Sleep(10000);
            }
        }

        Console.WriteLine("sleeping outside activities");
        Thread.Sleep(10000);
    }
}