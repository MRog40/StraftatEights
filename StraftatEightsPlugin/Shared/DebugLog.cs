using System.Diagnostics;

namespace StraftatEightsPlugin;

internal static class DebugLog
{
    [Conditional("STRAFTAT_ENABLE_DEBUG_LOGGING")]
    internal static void Info(string message)
    {
    }

    [Conditional("STRAFTAT_ENABLE_DEBUG_LOGGING")]
    internal static void Every(string key, float intervalSeconds, string message)
    {
    }

    [Conditional("STRAFTAT_ENABLE_DEBUG_LOGGING")]
    internal static void Reset()
    {
    }
}
