using System.Text.Encodings.Web;
using System.Text.Json;

namespace GR.OpenTelemetry.Processor.Partial;

public class SpecHelper
{
    public static string Json(TracesData tracesData) =>
        JsonSerializer.Serialize(tracesData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false, // For pretty printing
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // To allow special characters
            DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition
                    .WhenWritingNull
        });

    private const long UnixEpochTicks = 719162L /*Number of days from 1/1/0001 to 12/31/1969*/ * 10000 * 1000 * 60 * 60 * 24; /* Ticks per day.*/
    
    public static ulong ToUnixTimeNanoseconds(DateTime dateTime)
    {
        dateTime = dateTime.ToUniversalTime();

        long ticksSinceEpoch = dateTime.Ticks - UnixEpochTicks;

        // Convert to nanoseconds
        return (ulong)(ticksSinceEpoch * TimeSpan.NanosecondsPerTick);
    }
}