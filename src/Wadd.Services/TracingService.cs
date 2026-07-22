using System.Diagnostics;
using Wadd.Core.Interfaces;

namespace Wadd.Services;

public class TracingService : ITracingService
{
    public void TraceInformation(string message, IDictionary<string, string>? properties = null)
    {
        Trace.WriteLine($"[INFO] {message}");
    }

    public void TraceWarning(string message, IDictionary<string, string>? properties = null)
    {
        Trace.WriteLine($"[WARN] {message}");
    }

    public void TraceError(Exception exception, string message, IDictionary<string, string>? properties = null)
    {
        Trace.WriteLine($"[ERROR] {message}: {exception}");
    }
}
