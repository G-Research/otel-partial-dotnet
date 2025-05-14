using System.Diagnostics;

namespace GR.OpenTelemetry.Processor.Partial;

public class ResourceSpans
{
    public ResourceSpans(Activity activity, global::OpenTelemetry.Resources.Resource resource,
        TracesData.Signal signal)
    {
        Resource = new Resource(resource);
        ScopeSpans.Add(new ScopeSpans(activity, signal));
    }

    public Resource? Resource { get; set; }
    public List<ScopeSpans> ScopeSpans { get; set; } = [];
    // TODO missing mapping?
    public string? SchemaUrl { get; set; }
    
}