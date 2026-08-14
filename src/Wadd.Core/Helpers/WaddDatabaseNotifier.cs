using System;

namespace Wadd.Core.Helpers;

public static class WaddDatabaseNotifier
{
    public static event EventHandler? DataChanged;

    public static void NotifyDataChanged()
    {
        DataChanged?.Invoke(null, EventArgs.Empty);
    }
}
