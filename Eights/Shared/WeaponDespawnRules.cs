namespace Eights;

internal static class WeaponDespawnRules
{
    internal static bool ShouldPreserveDroppedWeapon(bool isDropped, bool needsAmmo,
        int currentAmmo, bool hasSpareRounds)
    {
        return isDropped && needsAmmo && currentAmmo <= 0 && hasSpareRounds;
    }
}