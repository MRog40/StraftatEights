using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class WeaponSettingsState
{
    internal static bool Enabled;
    internal static bool Cycle;
    internal static int SpareMagazines = 5;
    internal static List<string> Allowed = new();
    private static readonly Dictionary<int, string> SelectedWeapons = new();
    private static float _nextPushTime;
    internal static void Apply(bool enabled, string allowedWeapons, int spareMagazines, bool cycleWeapons)
    {
        Enabled = enabled; Cycle = cycleWeapons; SpareMagazines = spareMagazines; Allowed = WeaponService.ParseWeaponList(allowedWeapons);
    }
    private static void ApplyFromConfig() => Apply(Plugin.WeaponTweaksEnabled.Value, Plugin.AllowedWeapons.Value, Plugin.SpareMagazines.Value, Plugin.CycleWeapons.Value);
    internal static void PushIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost) return;
        ApplyFromConfig();
        MyceliumNetwork.RPC(Plugin.GlobalWeaponsModId, nameof(Plugin.SyncWeaponSettings), ReliableType.Reliable, Plugin.WeaponTweaksEnabled.Value, Plugin.AllowedWeapons.Value, Plugin.SpareMagazines.Value, Plugin.CycleWeapons.Value);
    }
    internal static void PeriodicPushIfHost() { if (HostSettingsSync.IsDue(ref _nextPushTime)) PushIfHost(); }
    internal static void OnLobbyEntered()
    {
        SelectedWeapons.Clear();
        if (MyceliumNetwork.IsHost) ApplyFromConfig();
    }
    internal static void OnPlayerEntered(CSteamID player)
    {
        if (MyceliumNetwork.IsHost) MyceliumNetwork.RPCTarget(Plugin.GlobalWeaponsModId, nameof(Plugin.SyncWeaponSettings), player, ReliableType.Reliable, Plugin.WeaponTweaksEnabled.Value, Plugin.AllowedWeapons.Value, Plugin.SpareMagazines.Value, Plugin.CycleWeapons.Value);
    }

    internal static void UpdateLocalCycle()
    {
        if (!Enabled || !Cycle || !Input.GetKeyDown(KeyCode.F8) || Allowed.Count == 0 || ClientInstance.Instance == null)
        {
            return;
        }
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            GiveCycledWeapon(ClientInstance.Instance.PlayerId);
        }
        else if (MyceliumNetwork.InLobby)
        {
            MyceliumNetwork.RPC(Plugin.GlobalWeaponsModId, nameof(Plugin.RequestWeaponCycle), ReliableType.Reliable, ClientInstance.Instance.PlayerId);
        }
    }

    internal static void GiveCycledWeapon(int playerId)
    {
        string? currentWeapon = GetSelectedWeapon(playerId);
        if (currentWeapon == null) return;

        int currentIndex = Allowed.IndexOf(currentWeapon);
        string nextWeapon = Allowed[(currentIndex + 1) % Allowed.Count];
        SelectedWeapons[playerId] = nextWeapon;
        WeaponService.GiveWeapon(playerId, nextWeapon, SpareMagazines);
    }

    internal static string? GetSelectedWeapon(int playerId)
    {
        if (Allowed.Count == 0)
        {
            return null;
        }

        if (SelectedWeapons.TryGetValue(playerId, out string selectedWeapon) && Allowed.Contains(selectedWeapon))
        {
            return selectedWeapon;
        }

        string initialWeapon = Allowed[0];
        SelectedWeapons[playerId] = initialWeapon;
        return initialWeapon;
    }
}