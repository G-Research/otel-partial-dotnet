using System.Diagnostics;

namespace GR.OpenTelemetry.Processor.Partial;

public class TracesData
{
    public List<ResourceSpans> ResourceSpans { get; set; } = [];

    public enum Signal
    {
        Heartbeat,
        Stop
    }
    
    
    public TracesData(Activity activity, global::OpenTelemetry.Resources.Resource resource,
        Signal signal)
    {
        ResourceSpans.Add(new ResourceSpans(activity, resource, signal));
    }
}