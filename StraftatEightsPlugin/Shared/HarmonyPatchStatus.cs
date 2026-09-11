using System;
using System.Collections.Generic;

namespace StraftatEightsPlugin;

internal static class HarmonyPatchStatus
{
    private static readonly Dictionary<Type, string> FailedPatches = new();
    private static readonly HashSet<GameMode> UnavailableWarnings = new();

    private static readonly Type[] RequiredCustomModePatches =
    {
        typeof(GameManager_GameModeDeath_Patch),
        typeof(GameManager_GameModeReset_Patch),
        typeof(PauseManager_RoundLifecycle_Patch),
        typeof(SceneMotor_GameModeCycle_Patch),
        typeof(GameManager_GameModeStart_Patch),
        typeof(ItemSpawner_WeaponPolicy_Patch),
        typeof(PlayerPickup_WeaponPolicy_Patch),
        typeof(PlayerPickup_RightHandDropPolicy_Patch),
        typeof(PlayerPickup_LeftHandDropPolicy_Patch),
        typeof(PlayerPickup_LeftHandFixPolicy_Patch),
        typeof(PlayerPickup_WeaponHandPolicy_Patch),
        typeof(PlayerManager_RespawnProtection_Patch),
        typeof(PlayerSetup_RespawnProtection_Patch),
        typeof(PlayerSetup_LocalHudRestore_Patch),
        typeof(Weapon_AmmoInitialization_Patch),
        typeof(FirstPersonController_MovementPolicy_Patch),
        typeof(Weapon_UpdatePolicy_Patch)
    };

    private static readonly Dictionary<GameMode, Type[]> RequiredModePatches = new()
    {
        [GameMode.Juggernaut] = new[]
        {
            typeof(GameManager_JuggernautTick_Patch),
            typeof(Minigun_JuggernautAmmo_Patch)
        },
        [GameMode.GunGame] = new[]
        {
            typeof(PlayerManager_GunGameSpawn_Patch)
        },
        [GameMode.SniperBattle] = new[]
        {
            typeof(PlayerManager_SniperBattleSpawn_Patch)
        },
        [GameMode.KillTheRat] = new[]
        {
            typeof(GameManager_KillTheRatTick_Patch),
            typeof(FirstPersonController_KillTheRatVoid_Patch),
            typeof(PlayerManager_KillTheRatSpawn_Patch)
        },
        [GameMode.OneInTheChamber] = new[]
        {
            typeof(PlayerManager_OneInTheChamberSpawn_Patch),
        },
        [GameMode.HotPotato] = new[]
        {
            typeof(PlayerManager_HotPotatoSpawn_Patch),
            typeof(MeleeWeapon_HotPotatoBatDamage_Patch)
        },
        [GameMode.Infidel] = new[]
        {
            typeof(PlayerManager_InfidelSpawn_Patch),
        },
        [GameMode.HVT] = new[]
        {
            typeof(GameManager_HVTTick_Patch)
        }
    };

    internal static void RecordFailure(Type patchType, string reason)
    {
        FailedPatches[patchType] = reason;
    }

    internal static bool IsModeAvailable(GameMode mode)
    {
        if (mode == GameMode.None || mode == GameMode.Default)
        {
            return true;
        }

        foreach (Type patchType in RequiredCustomModePatches)
        {
            if (FailedPatches.ContainsKey(patchType))
            {
                WarnUnavailable(mode, patchType, FailedPatches[patchType]);
                return false;
            }
        }

        if (!RequiredModePatches.TryGetValue(mode, out Type[]? requiredPatches))
        {
            return true;
        }

        foreach (Type patchType in requiredPatches)
        {
            if (FailedPatches.TryGetValue(patchType, out string? reason))
            {
                WarnUnavailable(mode, patchType, reason);
                return false;
            }
        }

        return true;
    }

    private static void WarnUnavailable(GameMode mode, Type patchType, string reason)
    {
        if (!UnavailableWarnings.Add(mode))
        {
            return;
        }

        Plugin.Logger.LogError($"[Harmony] Mode {mode} is disabled because patch {patchType.Name} failed: {reason}");
    }
}
