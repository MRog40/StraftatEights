using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

// Harmony patches for the Juggernaut game mode: kill/crown tracking, forced auto-respawn, and
// Juggernaut-only movement speed. See JuggernautState for the actual game-mode logic and
// JuggernautOutline for the visual outline effect.

[HarmonyPatch(typeof(GameManager), "Update")]
internal static class GameManager_JuggernautTick_Patch
{
    private static void Postfix(GameManager __instance)
    {
        if (__instance.IsServer)
        {
            if (GameModeManager.IsActive(GameMode.Juggernaut))
            {
                JuggernautState.ServerTick(Time.deltaTime);
            }
            JuggernautState.PeriodicPushSettingsIfHost();
        }
        JuggernautOutline.EnforceOutline();
    }
}

[HarmonyPatch(typeof(GameManager), "ResetGame")]
internal static class GameManager_JuggernautReset_Patch
{
    private static void Postfix()
    {
        JuggernautState.ResetMatchState();
        FFAState.ResetMatchState();
        JuggernautOutline.ResetState();
    }
}

[HarmonyPatch(typeof(GameManager), "RpcLogic___PlayerDied_3316948804")]
internal static class GameManager_JuggernautKill_Patch
{
    // Only ever runs on the server (its RpcReader gates on IsServer before calling this), matching
    // JuggernautState.OnServerKill/ServerTick's own host-only expectations.
    // NOTE: the numeric suffix is FishNet codegen from PlayerDied's signature - re-verify via
    // decompile if this stops firing after a game update (see AGENTS.md).
    private static void Postfix(int playerId)
    {
        if (!GameModeManager.IsActive(GameMode.Juggernaut))
        {
            return;
        }
        PlayerHealth? deadHealth = PlayerLookup.FindPlayerHealthById(playerId);
        int killerId = PlayerLookup.FindKillerId(deadHealth);
        JuggernautState.OnServerKill(playerId, killerId);
    }
}

[HarmonyPatch(typeof(PlayerHealth), "DespawnObject")]
internal static class PlayerHealth_JuggernautAutoRespawn_Patch
{
    // Forces the local player to respawn automatically after death instead of waiting on the normal
    // manual respawn UI, so the Juggernaut hunt never stalls waiting for someone to click it.
    private static void Postfix(PlayerHealth __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Juggernaut) || !__instance.IsOwner)
        {
            return;
        }
        PlayerManager? manager = GameModeRespawn.FindManager(__instance);
        if (manager != null && Plugin.Instance != null)
        {
            GameModeRespawn.Schedule(manager, JuggernautState.RespawnDelaySeconds);
        }
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Update")]
internal static class FirstPersonController_JuggernautSpeed_Patch
{
    // Runs after GlobalModifiers' own Update prefix sets movementFactor for this frame, then layers
    // the Juggernaut speed boost on top for whoever currently holds the crown - takes effect starting
    // next frame, same as every other per-frame tuning value in this project.
    private static void Postfix(FirstPersonController __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Juggernaut) || JuggernautState.CurrentJuggernautPlayerId < 0)
        {
            return;
        }

        PlayerHealth health = __instance.GetComponent<PlayerHealth>();
        if (health == null || health.playerValues == null || health.playerValues.playerClient == null)
        {
            return;
        }
        if (health.playerValues.playerClient.PlayerId != JuggernautState.CurrentJuggernautPlayerId)
        {
            return;
        }

        __instance.movementFactor *= JuggernautState.SpeedMultiplier;
    }
}
