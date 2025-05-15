using System.Diagnostics;

namespace GR.OpenTelemetry.Processor.Partial;

public class ScopeSpans
{
    public ScopeSpans(Activity activity, TracesData.Signal signal)
    {
        Scope = new InstrumentationScope(activity);
        Spans.Add(new Span(activity, signal));
        
    }

    public InstrumentationScope? Scope { get; set; }
    public List<Span> Spans { get; set; } = [];
    // TODO missing mapping?
    public string? SchemaUrl { get; set; }
}