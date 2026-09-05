using System.Collections.Generic;
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

    internal static void ApplySettings(bool enabled, string weaponOrder)
    {
        bool changed = Enabled != enabled;
        Enabled = enabled;
        WeaponOrder = WeaponService.ParseWeaponList(weaponOrder);
        if (changed) ResetMatchState();
    }

    private static void ApplyFromConfig() => ApplySettings(Plugin.GunGameEnabled.Value, Plugin.GunGameWeaponOrder.Value);
    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost) return;
        ApplyFromConfig();
        MyceliumNetwork.RPC(Plugin.GunGameModId, nameof(Plugin.SyncGunGameSettings), ReliableType.Reliable,
            Plugin.GunGameEnabled.Value, Plugin.GunGameWeaponOrder.Value);
    }
    internal static void PeriodicPushSettingsIfHost() { if (HostSettingsSync.IsDue(ref _nextSettingsPushTime)) PushSettingsIfHost(); }
    internal static void OnLobbyEntered() { if (MyceliumNetwork.IsHost) { ApplyFromConfig(); ResetMatchState(); } }
    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost) return;
        MyceliumNetwork.RPCTarget(Plugin.GunGameModId, nameof(Plugin.SyncGunGameSettings), player, ReliableType.Reliable,
            Plugin.GunGameEnabled.Value, Plugin.GunGameWeaponOrder.Value);
        MyceliumNetwork.RPCTarget(Plugin.GunGameModId, nameof(Plugin.SyncGunGameLiveState), player, ReliableType.Reliable, SerializeProgress());
    }
    internal static void ResetMatchState() => Progress.Clear();
    internal static void ApplyLiveState(string data)
    {
        Progress.Clear();
        foreach (string entry in (data ?? string.Empty).Split(';'))
        {
            int separator = entry.IndexOf(':');
            if (separator > 0 && int.TryParse(entry.Substring(0, separator), out int id) && int.TryParse(entry.Substring(separator + 1), out int progress)) Progress[id] = progress;
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
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost) MyceliumNetwork.RPC(Plugin.GunGameModId, nameof(Plugin.SyncGunGameLiveState), ReliableType.Reliable, SerializeProgress());
    }
}