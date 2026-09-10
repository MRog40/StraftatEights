using System;
using System.Collections.Generic;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class DebugLog
{
    private static readonly Dictionary<string, float> NextAllowedTimes = new(StringComparer.Ordinal);

    internal static bool Enabled => Plugin.DebugLogging?.Value == true;

    internal static void Info(string message)
    {
        if (Enabled)
        {
            Plugin.Logger.LogInfo("[Debug] " + message);
        }
    }

    internal static void Every(string key, float intervalSeconds, string message)
    {
        if (!Enabled)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (NextAllowedTimes.TryGetValue(key, out float nextAllowedTime) && now < nextAllowedTime)
        {
            return;
        }

        NextAllowedTimes[key] = now + intervalSeconds;
        Info(message);
    }

    internal static void Reset()
    {
        NextAllowedTimes.Clear();
    }
}
