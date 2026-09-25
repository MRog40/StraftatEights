using System;
using System.Collections.Generic;

namespace Eights;

internal static class HarmonyPatchStatus
{
    private static readonly Dictionary<Type, string> FailedPatches = new();
    private static readonly HashSet<GameMode> UnavailableWarnings = new();

    private static readonly Type[] RequiredCustomModePatches =
    {
        typeof(GameManager_GameModeDeath_Patch),
        typeof(GameManager_CustomTeamState_Patch),
        typeof(GameManager_GameModeReset_Patch),
        typeof(GameManager_PreRoundTimer_Patch),
        typeof(PauseManager_RoundLifecycle_Patch),
        typeof(SceneMotor_GameModeCycle_Patch),
        typeof(GameManager_GameModeStart_Patch),
        typeof(ScoreManager_CustomTeam_Patch),
        typeof(ItemSpawner_WeaponPolicy_Patch),
        typeof(PlayerPickup_WeaponPolicy_Patch),
        typeof(PlayerPickup_WeaponServerLogicPolicy_Patch),
        typeof(PlayerPickup_RightHandDropPolicy_Patch),
        typeof(PlayerPickup_LeftHandDropPolicy_Patch),
        typeof(PlayerPickup_LeftHandFixPolicy_Patch),
        typeof(PlayerPickup_WeaponHandPolicy_Patch),
        typeof(PlayerManager_RespawnProtection_Patch),
        typeof(PlayerSetup_RespawnProtection_Patch),
        typeof(PlayerSetup_LocalHudRestore_Patch),
        typeof(PlayerManager_CustomRoundStartScreen_Patch),
        typeof(Weapon_AmmoInitialization_Patch),
        typeof(FirstPersonController_MovementPolicy_Patch),
        typeof(FirstPersonController_PerPlayerSpeed_Patch),
        typeof(Weapon_UpdatePolicy_Patch)
    };

    private static readonly Dictionary<GameMode, Type[]> RequiredModePatches = new()
    {
        [GameMode.Juggertat] = new[]
        {
            typeof(Minigun_JuggertatAmmo_Patch)
        },
        [GameMode.Guntat] = new[]
        {
            typeof(PlayerManager_GuntatSpawn_Patch)
        },
        [GameMode.Snipertat] = new[]
        {
            typeof(PlayerManager_SnipertatSpawn_Patch)
        },
        [GameMode.Ratatat] = new[]
        {
            typeof(FirstPersonController_RatatatVoid_Patch),
            typeof(PlayerManager_RatatatSpawn_Patch)
        },
        [GameMode.Chambertat] = new[]
        {
            typeof(PlayerManager_ChambertatSpawn_Patch),
            typeof(MeleeWeapon_ChambertatKiller_Patch),
        },
        [GameMode.Potatotat] = new[]
        {
            typeof(PlayerManager_PotatotatSpawn_Patch)
        },
        [GameMode.Infideltat] = new[]
        {
            typeof(PlayerManager_InfideltatSpawn_Patch),
        },
        [GameMode.Assassintat] = new[]
        {
            typeof(PlayerManager_AssassintatSpawn_Patch),
        },
        [GameMode.Michaeltat] = new[]
        {
            typeof(PlayerManager_MichaeltatSpawn_Patch),
        },
        [GameMode.Tanktat] = new[]
        {
            typeof(FirstPersonController_TankCrouch_Patch)
        },
    };

    internal static void RecordFailure(Type patchType, string reason)
    {
        FailedPatches[patchType] = reason;
    }

    internal static bool IsModeAvailable(GameMode mode)
    {
        if (mode == GameMode.None)
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
