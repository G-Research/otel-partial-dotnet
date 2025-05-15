namespace GR.OpenTelemetry.Processor.Partial;

public class AnyValue
{
    public AnyValue(string? stringValue)
    {
        StringValue = stringValue;
    }

    public AnyValue(long? intValue)
    {
        IntValue = intValue;
    }

    public AnyValue(double? doubleValue)
    {
        DoubleValue = doubleValue;
    }

    public AnyValue(bool? boolValue)
    {
        BoolValue = boolValue;
    }

    public string? StringValue { get; set; }
    public long? IntValue { get; set; }
    public double? DoubleValue { get; set; }
    public bool? BoolValue { get; set; }
}