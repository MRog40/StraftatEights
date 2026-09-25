using System.Collections.Generic;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class FfatatState
{
    internal const string SettingsLobbyDataKey = "Eights_Ffatat_Settings";
    internal const string LiveLobbyDataKey = "Eights_Ffatat_Live";
    internal static bool Enabled;
    internal static int KillsToWin => GameModeManager.EffectivePointsToWin;
    internal static int WinnerId = -1;
    internal static readonly Dictionary<int, int> Kills = new();

    private static readonly ModeSyncState Sync = new();

    internal static void ApplySettings(bool enabled)
    {
        if (GameModeManager.ShouldDeferModeDisable(GameMode.Ffatat, enabled))
        {
            return;
        }

        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.FfatatEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.FfatatEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.FfatatModId, nameof(Plugin.SyncFfatatSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision, Plugin.FfatatEnabled.Value);
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
            PushSettingsIfHost();
            BroadcastLiveState();
        }
        else
        {
            ApplyLobbySettingsSnapshot();
            ApplyLobbyLiveSnapshot();
        }
    }

    internal static void PollLiveStateIfClient()
    {
        ApplyLobbyLiveSnapshot();
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
        MyceliumNetwork.RPCTarget(Plugin.FfatatModId, nameof(Plugin.SyncFfatatSettings), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision, Plugin.FfatatEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.FfatatModId, nameof(Plugin.SyncFfatatLiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, SerializeKills(), WinnerId, GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static void OnPlayerLeft(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        int playerId = PlayerLookup.FindPlayerId(player);
        if (playerId >= 0 && Kills.Remove(playerId)
            && GameModeManager.IsActive(GameMode.Ffatat))
        {
            BroadcastLiveState();
        }
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

    internal static void ApplyLiveState(CSteamID hostId, string killsData, int winnerId, int roundId,
        int revision, string source = "rpc")
    {
        if (winnerId < -1)
        {
            return;
        }
        if (!Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
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
        int totalKills = ScoreRules.AddPoints(currentKills, ScoreRules.PointsPerKill,
            KillsToWin);
        Kills[killerId] = totalKills;
        int awardedKills = totalKills - currentKills;
        if (awardedKills > 0)
        {
            GameModeHud.ShowScorePopupForPlayer(killerId, awardedKills);
        }
        if (totalKills >= KillsToWin)
        {
            WinnerId = killerId;
            Announce(PlayerLookup.GetPlayerNameTag(killerId) + " reached " + KillsToWin + " points and won the round!");
            GameModeManager.CompleteCustomRound(TeamAssignment.ResolveTeamId(killerId));
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
            int revision = Sync.NextLiveRevision();
            ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
                GameModeManager.RoundId, revision, SerializeKills(), WinnerId.ToString());
            MyceliumNetwork.RPC(Plugin.FfatatModId, nameof(Plugin.SyncFfatatLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeKills(), WinnerId, GameModeManager.RoundId,
                revision);
        }
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("ffatat", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 2, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[1], out int winnerId))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], winnerId, roundId, revision,
            ModeLobbyDataSync.Source("ffatat", "live"));
    }

    private static void Announce(string text)
    {
        GameModeHud.BroadcastAnnouncement(text);
    }
}