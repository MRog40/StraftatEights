using System.Collections.Generic;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class FFAState
{
    internal static bool Enabled;
    internal static int KillsToWin => GameModeManager.EffectivePointsToWin;
    internal static int WinnerId = -1;
    internal static readonly Dictionary<int, int> Kills = new();

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

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.FFAEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplySettingsFromHostConfig();
        MyceliumNetwork.RPC(Plugin.FFAModId, nameof(Plugin.SyncFFASettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.NextSettingsRevision(), Plugin.FFAEnabled.Value);
    }

    internal static void PeriodicPushSettingsIfHost()
    {
        if (!Sync.IsSettingsPushDue())
        {
            return;
        }
        PushSettingsIfHost();
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
        }
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        MyceliumNetwork.RPCTarget(Plugin.FFAModId, nameof(Plugin.SyncFFASettings), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision, Plugin.FFAEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.FFAModId, nameof(Plugin.SyncFFALiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, SerializeKills(), WinnerId, GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        WinnerId = -1;
        Kills.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, string killsData, int winnerId, int roundId, int revision)
    {
        if (winnerId < -1)
        {
            return;
        }
        if (!Sync.TryAcceptLiveSnapshot(hostId, roundId, revision))
        {
            return;
        }
        WinnerId = winnerId;
        Kills.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(killsData, KillsToWin))
        {
            Kills[entry.Key] = entry.Value;
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || WinnerId >= 0 || killerId < 0 || killerId == deadPlayerId)
        {
            return;
        }

        Kills.TryGetValue(killerId, out int currentKills);
        int totalKills = currentKills + ScoreRules.PointsPerKill;
        Kills[killerId] = totalKills;
        GameModeHud.ShowScorePopupForPlayer(killerId, ScoreRules.PointsPerKill);
        if (totalKills >= KillsToWin)
        {
            WinnerId = killerId;
            Announce(PlayerLookup.GetPlayerNameTag(killerId) + " reached " + KillsToWin + " points and won the round!");
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(killerId));
        }
        BroadcastLiveState();
    }

    internal static string SerializeKills()
    {
        return ScoreCodec.Serialize(Kills);
    }

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.FFAModId, nameof(Plugin.SyncFFALiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeKills(), WinnerId, GameModeManager.RoundId,
                Sync.NextLiveRevision());
        }
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.FFAModId, nameof(Plugin.FFAAnnounce), ReliableType.Reliable, text);
        }
    }
}