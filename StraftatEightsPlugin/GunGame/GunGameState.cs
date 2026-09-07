using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

internal static class GunGameState
{
    internal static bool Enabled;
    internal static readonly Dictionary<int, int> Progress = new();
    internal static List<string> WeaponOrder { get; private set; } = new();
    internal static int ScoreLimit => WeaponOrder.Count;
    private static float _nextSettingsPushTime;
    private static float _nextLiveStatePushTime;
    private static int _settingsRevision;
    private static int _lastSettingsRoundId = -1;
    private static int _lastSettingsRevision = -1;
    private static int _liveStateRevision;
    private static int _lastLiveStateRoundId = -1;
    private static int _lastLiveStateRevision = -1;

    internal static void ApplySettings(bool enabled, string weaponOrder)
    {
        List<string> nextWeaponOrder = WeaponService.ParseWeaponList(weaponOrder);
        bool changed = Enabled != enabled || !WeaponOrder.SequenceEqual(nextWeaponOrder, StringComparer.Ordinal);
        Enabled = enabled;
        WeaponOrder = nextWeaponOrder;
        if (changed) ResetMatchState();
    }

    private static void ApplyFromConfig() => ApplySettings(Plugin.GunGameEnabled.Value, Plugin.GunGameWeaponOrder.Value);
    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost) return;
        ApplyFromConfig();
        MyceliumNetwork.RPC(Plugin.GunGameModId, nameof(Plugin.SyncGunGameSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, ++_settingsRevision,
            Plugin.GunGameEnabled.Value, Plugin.GunGameWeaponOrder.Value);
    }
    internal static void PeriodicPushSettingsIfHost() { if (HostSettingsSync.IsDue(ref _nextSettingsPushTime)) PushSettingsIfHost(); }
       internal static void PeriodicPushIfHost()
       {
           PeriodicPushSettingsIfHost();
           if (HostSettingsSync.IsDue(ref _nextLiveStatePushTime)) BroadcastLiveState();
       }
    internal static void OnLobbyEntered()
    {
        _lastSettingsRoundId = -1;
        _lastSettingsRevision = -1;
        if (MyceliumNetwork.IsHost) { ApplyFromConfig(); ResetMatchState(); }
    }
    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost) return;
        MyceliumNetwork.RPCTarget(Plugin.GunGameModId, nameof(Plugin.SyncGunGameSettings), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, _settingsRevision,
            Plugin.GunGameEnabled.Value, Plugin.GunGameWeaponOrder.Value);
        MyceliumNetwork.RPCTarget(Plugin.GunGameModId, nameof(Plugin.SyncGunGameLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SerializeProgress(), GameModeManager.RoundId,
            _liveStateRevision);
    }
    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastSettingsRoundId, ref _lastSettingsRevision);
    }
    internal static void ResetMatchState()
    {
        _liveStateRevision++;
        _lastLiveStateRoundId = -1;
        _lastLiveStateRevision = -1;
        Progress.Clear();
    }
    internal static void ApplyLiveState(CSteamID hostId, string data, int roundId, int revision)
    {
        if (!SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ref _lastLiveStateRoundId, ref _lastLiveStateRevision))
        {
            return;
        }
        Progress.Clear();
        foreach (string entry in (data ?? string.Empty).Split(';'))
        {
            int separator = entry.IndexOf(':');
            if (separator > 0 && int.TryParse(entry.Substring(0, separator), out int id)
                && int.TryParse(entry.Substring(separator + 1), out int progress)
                && id >= 0 && progress >= 0 && progress <= ScoreLimit)
            {
                Progress[id] = progress;
            }
        }
    }
    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || killerId < 0 || killerId == deadPlayerId) return;
        Progress.TryGetValue(killerId, out int current);
        int next = current + 1;
        Progress[killerId] = next;
        if (ScoreLimit > 0 && next >= ScoreLimit) GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(killerId));
        else if (WeaponOrder.Count > 0) WeaponService.GiveWeapon(killerId, WeaponOrder[System.Math.Min(next, WeaponOrder.Count - 1)]);
        BroadcastLiveState();
    }
    internal static void GiveStartingWeapon(int playerId)
    {
        if (WeaponOrder.Count > 0) WeaponService.GiveWeapon(playerId, WeaponOrder[0]);
    }
    internal static string SerializeProgress()
    {
        StringBuilder result = new();
        foreach (KeyValuePair<int, int> entry in Progress)
        {
            if (result.Length > 0) result.Append(';');
            result.Append(entry.Key).Append(':').Append(entry.Value);
        }
        return result.ToString();
    }
    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            _liveStateRevision++;
            MyceliumNetwork.RPC(Plugin.GunGameModId, nameof(Plugin.SyncGunGameLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeProgress(), GameModeManager.RoundId, _liveStateRevision);
        }
    }
}