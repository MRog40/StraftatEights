using HarmonyLib;

namespace StraftatEightsPlugin;

internal static class WeaponReloadGuards
{
    internal static bool CanFire(Weapon weapon) => !WeaponAmmoTuning.IsReloading(weapon);
}

[HarmonyPatch(typeof(BeamGun), "FireBlast")]
internal static class BeamGun_FireBlast_Reload_Patch
{
    private static bool Prefix(BeamGun __instance) => WeaponReloadGuards.CanFire(__instance);
}

[HarmonyPatch(typeof(BeamGun), "Fire")]
internal static class BeamGun_Fire_Reload_Patch
{
    private static bool Prefix(BeamGun __instance) => WeaponReloadGuards.CanFire(__instance);
}

[HarmonyPatch(typeof(Gun), "Fire")]
internal static class Gun_Fire_Reload_Patch
{
    private static bool Prefix(Gun __instance) => WeaponReloadGuards.CanFire(__instance);
}

[HarmonyPatch(typeof(Shotgun), "Fire")]
internal static class Shotgun_Fire_Reload_Patch
{
    private static bool Prefix(Shotgun __instance) => WeaponReloadGuards.CanFire(__instance);
}

[HarmonyPatch(typeof(LargeRaycastGun), "Fire")]
internal static class LargeRaycastGun_Fire_Reload_Patch
{
    private static bool Prefix(LargeRaycastGun __instance) => WeaponReloadGuards.CanFire(__instance);
}

[HarmonyPatch(typeof(Minigun), "Fire")]
internal static class Minigun_Fire_Reload_Patch
{
    private static bool Prefix(Minigun __instance) => WeaponReloadGuards.CanFire(__instance);
}

[HarmonyPatch(typeof(RepulsiveGun), "Fire")]
internal static class RepulsiveGun_Fire_Reload_Patch
{
    private static bool Prefix(RepulsiveGun __instance) => WeaponReloadGuards.CanFire(__instance);
}

[HarmonyPatch(typeof(FlashLight), "Fire")]
internal static class FlashLight_Fire_Reload_Patch
{
    private static bool Prefix(FlashLight __instance) => WeaponReloadGuards.CanFire(__instance);
}

[HarmonyPatch(typeof(ChargeGun), "Fire")]
internal static class ChargeGun_Fire_Reload_Patch
{
    private static bool Prefix(ChargeGun __instance) => WeaponReloadGuards.CanFire(__instance);
}

[HarmonyPatch(typeof(BumpGun), "Fire")]
internal static class BumpGun_Fire_Reload_Patch
{
    private static bool Prefix(BumpGun __instance) => WeaponReloadGuards.CanFire(__instance);
}

[HarmonyPatch(typeof(DualLauncher), "Fire")]
internal static class DualLauncher_Fire_Reload_Patch
{
    private static bool Prefix(DualLauncher __instance) => WeaponReloadGuards.CanFire(__instance);
}

[HarmonyPatch(typeof(Taser), "Fire")]
internal static class Taser_Fire_Reload_Patch
{
    private static bool Prefix(Taser __instance) => WeaponReloadGuards.CanFire(__instance);
}