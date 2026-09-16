using System;
using System.Collections.Generic;
using Wadd.Core.Interfaces;
using Wadd.Core.Logging;

namespace Wadd.Services;

public class TracingService : ITracingService
{
    public void TraceInformation(string message, IDictionary<string, string>? properties = null)
    {
        AppLogger.LogInfo("TracingService", message);
    }

    public void TraceWarning(string message, IDictionary<string, string>? properties = null)
    {
        AppLogger.LogWarning("TracingService", message);
    }

    public void TraceError(Exception exception, string message, IDictionary<string, string>? properties = null)
    {
        AppLogger.LogError("TracingService", message, exception);
    }
}
