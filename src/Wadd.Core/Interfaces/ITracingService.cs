namespace Wadd.Core.Interfaces;

/// <summary>
/// Placeholder service interface for future tracing and diagnostics features.
/// </summary>
public interface ITracingService
{
    void TraceInformation(string message, IDictionary<string, string>? properties = null);
    void TraceWarning(string message, IDictionary<string, string>? properties = null);
    void TraceError(Exception exception, string message, IDictionary<string, string>? properties = null);
}
