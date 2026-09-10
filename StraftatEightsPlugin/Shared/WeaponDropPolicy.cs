using System;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class WeaponDropPolicy
{
    internal static bool IsDropBlocked(PlayerPickup pickup, bool rightHand)
    {
        Weapon? weapon = GetHeldWeapon(pickup, rightHand);
        if (weapon == null)
        {
            return false;
        }

        if (!GameModeManager.ShouldIgnoreGlobalWeaponSettings
            && WeaponSettingsState.Enabled && WeaponSettingsState.Cycle)
        {
            return true;
        }

        PlayerHealth? health = pickup.GetComponent<PlayerHealth>();
        if (health == null)
        {
            return false;
        }

        int playerId = health.playerValues?.playerClient?.PlayerId ?? -1;
        switch (GameModeManager.ActiveMode)
        {
            case GameMode.GunGame:
                return GunGameState.Enabled;
            case GameMode.Juggernaut:
                return JuggernautState.IsCurrentJuggernaut(health)
                    && weapon.name.StartsWith(JuggernautState.WeaponName, StringComparison.Ordinal);
            case GameMode.MichaelMeyers:
                return MichaelMeyersState.IsCouperet(weapon)
                    && (MichaelMeyersState.CanHoldCouperet(health)
                        || MichaelMeyersState.CanHoldSurvivorWeapon(health));
            case GameMode.KillTheRat:
                return KillTheRatState.IsRat(health)
                    ? KillTheRatState.IsRatWeapon(weapon)
                    : KillTheRatState.IsHumanWeapon(weapon);
            case GameMode.OneInTheChamber:
                return (rightHand && OneInTheChamberState.IsPistol(weapon))
                    || (!rightHand && OneInTheChamberState.IsCouperet(weapon));
            case GameMode.HotPotato:
                return playerId >= 0 && HotPotatoState.IsAllowedWeapon(weapon, playerId);
            case GameMode.Infidel:
                return playerId >= 0
                    && weapon.name.StartsWith(InfidelState.WeaponName, StringComparison.Ordinal);
            case GameMode.SniperBattle:
                return SniperBattleState.IsSniperWeapon(weapon);
            default:
                return false;
        }
    }

    private static Weapon? GetHeldWeapon(PlayerPickup pickup, bool rightHand)
    {
        if (pickup == null)
        {
            return null;
        }

        GameObject? heldObject = rightHand ? pickup.objInHand : pickup.objInLeftHand;
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }
}