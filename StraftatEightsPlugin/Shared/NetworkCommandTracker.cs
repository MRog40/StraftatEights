using System.Collections.Generic;
using Steamworks;

namespace StraftatEightsPlugin;

internal sealed class NetworkCommandTracker
{
    private readonly Dictionary<ulong, int> _lastCommandBySender = new();

    internal bool TryAccept(CSteamID sender, int commandId)
    {
        if (sender.m_SteamID == 0 || commandId < 0)
        {
            return false;
        }

        if (_lastCommandBySender.TryGetValue(sender.m_SteamID, out int lastCommandId)
            && commandId <= lastCommandId)
        {
            return false;
        }

        _lastCommandBySender[sender.m_SteamID] = commandId;
        return true;
    }

    internal void Remove(CSteamID sender)
    {
        _lastCommandBySender.Remove(sender.m_SteamID);
    }

    internal void Clear()
    {
        _lastCommandBySender.Clear();
    }
}
