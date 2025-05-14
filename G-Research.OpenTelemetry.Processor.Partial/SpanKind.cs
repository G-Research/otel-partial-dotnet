namespace GR.OpenTelemetry.Processor.Partial;

public enum SpanKind
{
    SpanKindUnspecified = 0,
    SpanKindInternal = 1,
    SpanKindServer = 2,
    SpanKindClient = 3,
    SpanKindProducer = 4,
    SpanKindConsumer = 5
}