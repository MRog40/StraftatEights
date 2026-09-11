using System;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HVTState
{
    internal static bool Enabled;
    internal static int CurrentHVTPlayerId = -1;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static int WinnerId = -1;
    internal static readonly Dictionary<int, int> Points = new();
    internal const string SettingsLobbyDataKey = "StraftatEights_HVT_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_HVT_Live";

    private static float _survivalAccumulator;
    private static readonly ModeSyncState Sync = new();

    internal static void ApplySettings(bool enabled)
    {
        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.HVTEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        PublishSettingsSnapshot(revision);
        MyceliumNetwork.RPC(Plugin.HVTModId, nameof(Plugin.SyncHVTSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.HVTEnabled.Value);
    }

    internal static void PeriodicPushSettingsIfHost()
    {
        if (Sync.IsSettingsPushDue())
        {
            PushSettingsIfHost();
        }
    }

    internal static void PeriodicPushIfHost()
    {
        PeriodicPushSettingsIfHost();
        if (Sync.IsLivePushDue())
        {
            BroadcastLiveState();
        }
    }

    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        if (MyceliumNetwork.IsHost)
        {
            ApplySettingsFromHostConfig();
            ResetMatchState();
            PushSettingsIfHost();
            BroadcastLiveState();
        }
        else
        {
            ApplyLobbySettingsSnapshot();
            ApplyLobbyLiveSnapshot();
        }
    }

    internal static void OnLobbyDataUpdated(List<string> keys)
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby)
        {
            return;
        }

        if (ModeLobbyDataSync.ContainsKey(keys, SettingsLobbyDataKey))
        {
            ApplyLobbySettingsSnapshot();
        }
        if (ModeLobbyDataSync.ContainsKey(keys, LiveLobbyDataKey))
        {
            ApplyLobbyLiveSnapshot();
        }
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.HVTModId, nameof(Plugin.SyncHVTSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId,
            Sync.SettingsRevision, Plugin.HVTEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.HVTModId, nameof(Plugin.SyncHVTLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, CurrentHVTPlayerId,
            SerializePoints(), WinnerId, GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        _survivalAccumulator = 0f;
        CurrentHVTPlayerId = -1;
        WinnerId = -1;
        Points.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, int hvtPlayerId, string pointsData,
        int winnerId, int roundId, int revision, string source = "rpc")
    {
        if (hvtPlayerId < -1 || winnerId < -1
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        DebugLog.Info($"HVT live state accepted source={source} host={hostId.m_SteamID} "
            + $"round={roundId} revision={revision} hvt={hvtPlayerId} points={Points.Count}");

        CurrentHVTPlayerId = hvtPlayerId;
        WinnerId = winnerId;
        Points.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(pointsData, PointsToWin))
        {
            Points[entry.Key] = entry.Value;
        }
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.HVT)
            || !MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost
            || CurrentHVTPlayerId < 0 || WinnerId >= 0)
        {
            return;
        }

        _survivalAccumulator += Mathf.Max(0f, deltaTime);
        int seconds = Mathf.FloorToInt(_survivalAccumulator);
        if (seconds <= 0)
        {
            return;
        }

        _survivalAccumulator -= seconds;
        for (int second = 0; second < seconds && WinnerId < 0; second++)
        {
            AwardPoints(CurrentHVTPlayerId, ScoreRules.PointsPerHVTSurvivalSecond);
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.HVT) || WinnerId >= 0)
        {
            return;
        }

        bool legitKill = killerId >= 0 && killerId != deadPlayerId;
        if (CurrentHVTPlayerId < 0)
        {
            if (legitKill)
            {
                BecomeHVT(killerId, PlayerLookup.GetPlayerNameTag(killerId)
                    + " got the first kill and is now the <color=#FFD700><b>HVT</b></color>!");
            }
            return;
        }

        if (deadPlayerId == CurrentHVTPlayerId)
        {
            if (legitKill)
            {
                BecomeHVT(killerId, PlayerLookup.GetPlayerNameTag(killerId)
                    + " killed the HVT and is now the <color=#FFD700><b>HVT</b></color>!");
            }
            else
            {
                ClearHVT();
            }
        }
    }

    private static bool AwardPoints(int playerId, int amount)
    {
        Points.TryGetValue(playerId, out int current);
        int total = current + amount;
        Points[playerId] = total;
        GameModeHud.ShowScorePopupForPlayer(playerId, amount);
        if (total >= PointsToWin)
        {
            WinnerId = playerId;
            Announce(PlayerLookup.GetPlayerNameTag(playerId) + " reached " + PointsToWin
                + " points and won the HVT round!");
            BroadcastLiveState();
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(playerId));
            return false;
        }

        BroadcastLiveState();
        return true;
    }

    private static void BecomeHVT(int playerId, string announcement)
    {
        CurrentHVTPlayerId = playerId;
        _survivalAccumulator = 0f;
        Announce(announcement);
        BroadcastLiveState();
    }

    private static void ClearHVT()
    {
        CurrentHVTPlayerId = -1;
        _survivalAccumulator = 0f;
        Announce("The HVT died without a killer. The next legitimate kill will select a new HVT.");
        BroadcastLiveState();
    }

    internal static string SerializePoints() => ScoreCodec.Serialize(Points);

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            int revision = Sync.NextLiveRevision();
            PublishLiveSnapshot(revision);
            MyceliumNetwork.RPC(Plugin.HVTModId, nameof(Plugin.SyncHVTLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, CurrentHVTPlayerId, SerializePoints(), WinnerId,
                GameModeManager.RoundId, revision);
        }
    }

    private static void PublishSettingsSnapshot(int revision)
    {
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.HVTEnabled.Value ? "1" : "0");
    }

    private static void PublishLiveSnapshot(int revision)
    {
        ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, CurrentHVTPlayerId.ToString(), WinnerId.ToString(),
            SerializePoints());
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled))
        {
            return;
        }

        if (Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ModeLobbyDataSync.Source("hvt", "settings")))
        {
            ApplySettings(enabled);
            Plugin.Logger.LogInfo($"[HVT] Accepted settings via lobby data: round={roundId} revision={revision}");
        }
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 3, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[0], out int hvtPlayerId)
            || !int.TryParse(fields[1], out int winnerId))
        {
            return;
        }

        ApplyLiveState(hostId, hvtPlayerId, fields[2], winnerId, roundId, revision,
            ModeLobbyDataSync.Source("hvt", "live"));
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.HVTModId, nameof(Plugin.HVTAnnounce), ReliableType.Reliable, text);
        }
    }
}
