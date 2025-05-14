using System.Diagnostics;

namespace GR.OpenTelemetry.Processor.Partial;

public class Status
{
    public Status(ActivityStatusCode activityStatus, string? activityStatusDescription)
    {
        Code = activityStatus switch
        {
            ActivityStatusCode.Unset => StatusCode.StatusCodeUnset,
            ActivityStatusCode.Ok => StatusCode.StatusCodeOk,
            ActivityStatusCode.Error => StatusCode.StatusCodeError,
            _ => StatusCode.StatusCodeUnset
        };
        Message = activityStatusDescription;
    }

    public enum StatusCode
    {
        StatusCodeUnset = 0,
        StatusCodeOk = 1,
        StatusCodeError = 2
    }
    
    public string? Message { get; set; }
    public StatusCode Code { get; set; } = StatusCode.StatusCodeUnset;
}