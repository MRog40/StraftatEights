using MyceliumNetworking;
using UnityEngine;

namespace StraftatEightsPlugin;

// Shared timing gate for host-authoritative settings retries. The callback remains feature-owned;
// this only makes retry timing independent of a particular game-mode update hook.
internal static class HostSettingsSync
{
    internal static bool IsDue(ref float nextPushTime)
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            nextPushTime = 0f;
            return false;
        }

        if (Time.unscaledTime < nextPushTime)
        {
            return false;
        }

        nextPushTime = Time.unscaledTime + 3f;
        return true;
    }
}