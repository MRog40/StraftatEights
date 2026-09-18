using System;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

internal static class PlayerNameSync
{
    internal const string LobbyDataKey = "StraftatEights_PlayerNames";
    private static readonly ModeSyncState Sync = new(livePushInterval: 1.5f);
    private static readonly Dictionary<int, string> Names = new();
    private static string _hostPayload = string.Empty;

    internal static void Initialize()
    {
        ModeLobbyDataSync.RegisterKeys(LobbyDataKey);
        MyceliumNetwork.LobbyCreated += OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += OnPlayerEntered;
    }

    internal static string GetDisplayName(int playerId)
    {
        return Names.TryGetValue(playerId, out string? name)
            ? name
            : ClientInstance.ReplaceAllPlayerNameTags(PlayerLookup.GetPlayerNameTag(playerId));
    }

    internal static void PeriodicPushIfHost()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost && Sync.IsLivePushDue())
        {
            BroadcastIfChanged(false);
        }
    }

    internal static void PollIfClient()
    {
        if (MyceliumNetwork.InLobby && !MyceliumNetwork.IsHost)
        {
            ApplyLobbySnapshot();
        }
    }

    internal static void ApplySnapshot(CSteamID hostId, int roundId, int revision,
        string payload, string source)
    {
        if (!PlayerNameSnapshotCodec.TryDeserialize(payload, out Dictionary<int, string> names)
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        Names.Clear();
        foreach (KeyValuePair<int, string> entry in names)
        {
            Names[entry.Key] = entry.Value;
        }
    }

    private static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        Names.Clear();
        _hostPayload = string.Empty;
        if (MyceliumNetwork.IsHost)
        {
            BroadcastIfChanged(true);
        }
        else
        {
            ApplyLobbySnapshot();
        }
    }

    private static void OnLobbyLeft()
    {
        Sync.ResetForLobby();
        Names.Clear();
        _hostPayload = string.Empty;
    }

    private static void OnLobbyDataUpdated(List<string> keys)
    {
        if (!MyceliumNetwork.IsHost && ModeLobbyDataSync.ContainsKey(keys, LobbyDataKey))
        {
            ApplyLobbySnapshot();
        }
    }

    private static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        BroadcastIfChanged(false);
        if (_hostPayload.Length > 0 || Sync.LiveRevision > 0)
        {
            MyceliumNetwork.RPCTarget(GameModeManager.ModId,
                nameof(Plugin.SyncPlayerDisplayNames), player, ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.LiveRevision,
                _hostPayload);
        }
    }

    private static void ApplyLobbySnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LobbyDataKey, 1, out CSteamID hostId,
                out int roundId, out int revision, out string[] fields))
        {
            return;
        }

        ApplySnapshot(hostId, roundId, revision, fields[0], "lobby-data");
    }

    private static void BroadcastIfChanged(bool force)
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        string payload = SerializeHostNames();
        if (!force && payload == _hostPayload)
        {
            return;
        }

        _hostPayload = payload;
        int revision = Sync.NextLiveRevision();
        ModeLobbyDataSync.Publish(LobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, payload);
        MyceliumNetwork.RPC(GameModeManager.ModId,
            nameof(Plugin.SyncPlayerDisplayNames), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision, payload);
    }

    private static string SerializeHostNames()
    {
        Dictionary<int, string> names = new();
        foreach (KeyValuePair<int, ClientInstance> entry in ClientInstance.playerInstances)
        {
            if (entry.Key >= 0 && entry.Value != null && entry.Value)
            {
                names[entry.Key] = entry.Value.PlayerName ?? string.Empty;
            }
        }

        return PlayerNameSnapshotCodec.Serialize(names);
    }
}