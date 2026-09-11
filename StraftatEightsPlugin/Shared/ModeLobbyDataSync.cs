using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

internal static class ModeLobbyDataSync
{
    internal static void RegisterKeys(params string[] keys)
    {
        foreach (string key in keys)
        {
            MyceliumNetwork.RegisterLobbyDataKey(key);
        }
    }

    internal static bool ContainsKey(List<string> keys, string key)
    {
        foreach (string changedKey in keys)
        {
            if (changedKey == key)
            {
                return true;
            }
        }

        return false;
    }

    internal static string Read(string key)
    {
        return MyceliumNetwork.GetLobbyData<string>(key) ?? string.Empty;
    }

    internal static void Publish(string key, CSteamID hostId, int roundId, int revision,
        params string[] fields)
    {
        MyceliumNetwork.SetLobbyData(key, LobbySnapshotCodec.Build(hostId.m_SteamID,
            roundId, revision, fields));
    }

    internal static void PublishRaw(string key, string payload)
    {
        MyceliumNetwork.SetLobbyData(key, payload ?? string.Empty);
    }

    internal static bool TryRead(string key, int fieldCount, out CSteamID hostId,
        out int roundId, out int revision, out string[] fields)
    {
        bool parsed = LobbySnapshotCodec.TryParse(Read(key), fieldCount, out ulong hostSteamId,
            out roundId, out revision, out fields);
        hostId = new CSteamID(hostSteamId);
        return parsed;
    }

    internal static bool TryReadOrdered(string key, int partCount, int hostIndex,
        int roundIndex, int revisionIndex, out CSteamID hostId, out int roundId,
        out int revision, out string[] parts)
    {
        bool parsed = LobbySnapshotCodec.TryParseOrdered(Read(key), partCount, hostIndex,
            roundIndex, revisionIndex, out ulong hostSteamId, out roundId, out revision,
            out parts);
        hostId = new CSteamID(hostSteamId);
        return parsed;
    }

    internal static string Source(string mode, string channel)
    {
        return mode + "-" + channel + "-lobby-data";
    }
}
