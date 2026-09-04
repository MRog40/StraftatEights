using HarmonyLib;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(GameManager), "PlayerDied")]
internal static class GameManager_FFAKill_Patch
{
    private static bool Prefix(GameManager __instance, int playerId)
    {
        if (!GameModeManager.IsActive(GameMode.FreeForAll) || !__instance.IsServer)
        {
            return true;
        }
        FFAState.OnServerKill(playerId, PlayerLookup.FindKillerId(PlayerLookup.FindPlayerHealthById(playerId)));
        return false;
    }
}

[HarmonyPatch(typeof(PlayerHealth), "DespawnObject")]
internal static class PlayerHealth_FFAAutoRespawn_Patch
{
    private static void Postfix(PlayerHealth __instance)
    {
        if (!GameModeManager.IsActive(GameMode.FreeForAll) || !__instance.IsOwner)
        {
            return;
        }
        PlayerManager? manager = GameModeRespawn.FindManager(__instance);
        if (manager != null)
        {
            GameModeRespawn.Schedule(manager, FFAState.RespawnDelaySeconds);
        }
    }
}