using HarmonyLib;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Eights;

// Harmony patches for the Juggertat game mode: kill/crown tracking, forced auto-respawn, and
// Juggertat-only movement speed. See JuggertatState for the actual game-mode logic and
// PlayerOutline for the visual outline effect.

[HarmonyPatch(typeof(Minigun), "Update")]
internal static class Minigun_JuggertatAmmo_Patch
{
    private const int MagazineSize = 100;
    private static readonly ConditionalWeakTable<Minigun, object> ConfiguredMiniguns = new();

    private static void Prefix(Minigun __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Juggertat) || __instance == null
            || !JuggertatState.IsCurrentJuggertatWeapon(__instance))
        {
            return;
        }

        __instance.reloadWeapon = false;
        __instance.ammoCharge = MagazineSize;
        WeaponAmmoTuning.PreventAutoDespawn(__instance);
        if (!ConfiguredMiniguns.TryGetValue(__instance, out _))
        {
            __instance.currentAmmo = MagazineSize;
            __instance.chargedBullets = 0f;
            ConfiguredMiniguns.Add(__instance, new object());
        }

        WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance, MagazineSize);
        WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
    }

    private static void Postfix(Minigun __instance)
    {
        if (GameModeManager.IsActive(GameMode.Juggertat)
            && __instance != null
            && JuggertatState.IsCurrentJuggertatWeapon(__instance))
        {
            WeaponAmmoTuning.UpdateUnlimitedAmmoHud(__instance);
        }
    }
}
