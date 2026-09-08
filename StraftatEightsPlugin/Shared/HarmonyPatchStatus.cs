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
        typeof(PauseManager_GameModeLifecycle_Patch),
        typeof(SceneMotor_GameModeCycle_Patch),
        typeof(GameManager_GameModeStart_Patch)
    };

    private static readonly Dictionary<GameMode, Type[]> RequiredModePatches = new()
    {
        [GameMode.Juggernaut] = new[]
        {
            typeof(GameManager_JuggernautTick_Patch),
            typeof(FirstPersonController_JuggernautSpeed_Patch),
            typeof(FirstPersonController_JuggernautJump_Patch),
            typeof(Weapon_JuggernautMinigunAmmo_Patch),
            typeof(Minigun_JuggernautAmmoDisplay_Patch),
            typeof(Minigun_JuggernautReload_Patch),
            typeof(PlayerPickup_JuggernautWeapon_Patch)
        },
        [GameMode.GunGame] = new[]
        {
            typeof(PlayerManager_GunGameSpawn_Patch),
            typeof(Weapon_GunGameUnlimitedAmmo_Patch)
        },
        [GameMode.SniperBattle] = new[]
        {
            typeof(PlayerManager_SniperBattleSpawn_Patch),
            typeof(PlayerPickup_SniperBattleWeapon_Patch),
            typeof(Weapon_SniperBattleUnlimitedAmmo_Patch)
        },
        [GameMode.MichaelMeyers] = new[]
        {
            typeof(PauseManager_MichaelMeyersRoundStart_Patch),
            typeof(ItemSpawner_MichaelMeyers_Patch),
            typeof(PlayerPickup_MichaelMeyersWeapon_Patch),
            typeof(FirstPersonController_MichaelMeyersMovement_Patch),
            typeof(FirstPersonController_MichaelMeyersSlide_Patch),
            typeof(FirstPersonController_MichaelMeyersHandleSlide_Patch),
            typeof(FirstPersonController_MichaelMeyersWallJump_Patch),
            typeof(FirstPersonController_MichaelMeyersWallCollision_Patch),
            typeof(Weapon_MichaelMeyersMovement_Patch)
        },
        [GameMode.KillTheRat] = new[]
        {
            typeof(GameManager_KillTheRatTick_Patch),
            typeof(PlayerManager_KillTheRatSpawn_Patch),
            typeof(PlayerPickup_KillTheRatWeapon_Patch),
            typeof(Weapon_KillTheRatUnlimitedGlock_Patch),
            typeof(FirstPersonController_KillTheRatSpeed_Patch)
        },
        [GameMode.OneInTheChamber] = new[]
        {
            typeof(PauseManager_OneInTheChamberRoundStart_Patch),
            typeof(ItemSpawner_OneInTheChamber_Patch),
            typeof(PlayerManager_OneInTheChamberSpawn_Patch),
            typeof(PlayerPickup_OneInTheChamberWeapon_Patch),
            typeof(Weapon_OneInTheChamberAmmo_Patch)
        },
        [GameMode.HotPotato] = new[]
        {
            typeof(PauseManager_HotPotatoRoundStart_Patch),
            typeof(ItemSpawner_HotPotato_Patch),
            typeof(PlayerManager_HotPotatoSpawn_Patch),
            typeof(PlayerPickup_HotPotatoWeapon_Patch)
        },
        [GameMode.Infidel] = new[]
        {
            typeof(PauseManager_InfidelRoundStart_Patch),
            typeof(ItemSpawner_Infidel_Patch),
            typeof(PlayerManager_InfidelSpawn_Patch),
            typeof(PlayerPickup_InfidelWeapon_Patch),
            typeof(FirstPersonController_InfidelMovement_Patch),
            typeof(FirstPersonController_InfidelSlide_Patch),
            typeof(FirstPersonController_InfidelHandleSlide_Patch),
            typeof(FirstPersonController_InfidelJump_Patch),
            typeof(FirstPersonController_InfidelWallCollision_Patch)
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
