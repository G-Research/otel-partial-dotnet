using GR.OpenTelemetry.Processor.Partial;
using Xunit;

public class AnyValueTests
{
    [Fact]
    public void Constructor_ShouldSetStringValue()
    {
        var stringValue = "test";

        var anyValue = new AnyValue(stringValue);

        Assert.Equal(stringValue, anyValue.StringValue);
        Assert.Null(anyValue.IntValue);
        Assert.Null(anyValue.DoubleValue);
        Assert.Null(anyValue.BoolValue);
    }

    [Fact]
    public void Constructor_ShouldSetIntValue()
    {
        long intValue = 123;

        var anyValue = new AnyValue(intValue);

        Assert.Equal(intValue, anyValue.IntValue);
        Assert.Null(anyValue.StringValue);
        Assert.Null(anyValue.DoubleValue);
        Assert.Null(anyValue.BoolValue);
    }

    [Fact]
    public void Constructor_ShouldSetDoubleValue()
    {
        double doubleValue = 123.45;

        var anyValue = new AnyValue(doubleValue);

        Assert.Equal(doubleValue, anyValue.DoubleValue);
        Assert.Null(anyValue.StringValue);
        Assert.Null(anyValue.IntValue);
        Assert.Null(anyValue.BoolValue);
    }

    [Fact]
    public void Constructor_ShouldSetBoolValue()
    {
        bool boolValue = true;

        var anyValue = new AnyValue(boolValue);

        Assert.Equal(boolValue, anyValue.BoolValue);
        Assert.Null(anyValue.StringValue);
        Assert.Null(anyValue.IntValue);
        Assert.Null(anyValue.DoubleValue);
    }
}