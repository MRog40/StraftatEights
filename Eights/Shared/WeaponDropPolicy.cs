using System;
using UnityEngine;

namespace Eights;

internal static class WeaponDropPolicy
{
    internal static bool IsDropBlocked(PlayerPickup pickup, bool rightHand)
    {
        if (GameModeManager.IsVanillaScene)
        {
            return false;
        }

        Weapon? weapon = GetHeldWeapon(pickup, rightHand);
        if (weapon == null)
        {
            return false;
        }

        if (WeaponPolicy.IsFixedHandGrenade(weapon))
        {
            return true;
        }

        PlayerHealth? health = pickup.GetComponent<PlayerHealth>();
        int playerId = pickup.playerValues?.playerClient?.PlayerId
            ?? health?.playerValues?.playerClient?.PlayerId ?? -1;
        if (GameModeManager.ActiveMode == GameMode.Infectedtat)
        {
            playerId = PlayerLookup.FindPlayerId(pickup);
            return playerId < 0 || InfectedtatState.IsInfectedtat(playerId);
        }

        if (health == null)
        {
            return false;
        }

        switch (GameModeManager.ActiveMode)
        {
            case GameMode.Ninjatat:
            case GameMode.Hunttat:
            case GameMode.Tanktat:
                return playerId >= 0 && HuntModesState.IsExpectedWeapon(weapon, playerId);
            case GameMode.Guntat:
                return GuntatState.Enabled;
            case GameMode.Juggertat:
                return JuggertatState.IsCurrentJuggertatWeapon(weapon);
            case GameMode.Michaeltat:
                return MichaeltatState.IsCouperet(weapon)
                    && (MichaeltatState.CanHoldCouperet(health)
                        || MichaeltatState.CanHoldSurvivorWeapon(health));
            case GameMode.Ratatat:
                return RatatatState.IsRat(health)
                    ? RatatatState.IsRatWeapon(weapon)
                    : RatatatState.IsHumanWeapon(weapon);
            case GameMode.Chambertat:
                return ChambertatState.IsPistol(weapon)
                    || ChambertatState.IsCouperet(weapon);
            case GameMode.Potatotat:
                return playerId >= 0 && PotatotatState.IsAllowedWeapon(weapon, playerId);
            case GameMode.Infideltat:
                return playerId >= 0
                    && weapon.name.StartsWith(InfideltatState.WeaponName, StringComparison.Ordinal);
            case GameMode.Assassintat:
                return playerId >= 0 && AssassintatState.IsAllowedWeapon(weapon, playerId);
            case GameMode.PotatoInftat:
                return playerId >= 0 && PotatoInftatState.IsInfectedtat(playerId);
            case GameMode.Snipertat:
                return SnipertatState.IsSniperWeapon(weapon);
            case GameMode.Nifetat:
                return NifetatState.IsSelectedWeapon(weapon);
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