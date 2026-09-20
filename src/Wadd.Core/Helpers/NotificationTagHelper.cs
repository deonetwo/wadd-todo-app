using System;

namespace Wadd.Core.Helpers;

/// <summary>
/// Provides deterministic notification ID calculation for task reminders and notifications.
/// Prevents randomized string hash code discrepancies across different processes.
/// </summary>
public static class NotificationTagHelper
{
    public const int TasksSummaryNotificationId = 90001;

    /// <summary>
    /// Computes a deterministic integer notification ID from a tag.
    /// - If tag is a Guid string (e.g. task ID), parses to Guid and returns its deterministic Guid.GetHashCode().
    /// - If tag is "tasks-summary", returns the constant TasksSummaryNotificationId (90001).
    /// - For any other non-Guid string, computes a deterministic FNV-1a 32-bit hash.
    /// - If tag is null or whitespace, generates a time-based ID.
    /// </summary>
    public static int GetNotificationId(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return (int)DateTime.UtcNow.Ticks;
        }

        if (Guid.TryParse(tag, out var guid))
        {
            return guid.GetHashCode();
        }

        if (string.Equals(tag, "tasks-summary", StringComparison.OrdinalIgnoreCase))
        {
            return TasksSummaryNotificationId;
        }

        // Deterministic 32-bit FNV-1a hash to avoid randomized per-process string.GetHashCode()
        uint hash = 2166136261;
        foreach (char c in tag)
        {
            hash = (hash ^ c) * 16777619;
        }
        return (int)hash;
    }
}
