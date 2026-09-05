using HarmonyLib;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(GameManager), "RpcLogic___PlayerDied_3316948804")]
internal static class GameManager_FFAKill_Patch
{
    private static bool Prefix(GameManager __instance, int playerId)
    {
        if (!GameModeManager.IsActive(GameMode.FreeForAll) || !__instance.IsServer)
        {
            return true;
        }
        int killerId = PlayerLookup.FindKillerId(PlayerLookup.FindPlayerHealthById(playerId));
        Plugin.Logger.LogInfo($"[FFA] Server death: deadPlayer={playerId} killer={killerId}; scheduling respawn");
        FFAState.OnServerKill(playerId, killerId);
        GameModeRespawn.Schedule(playerId, FFAState.RespawnDelaySeconds);
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
        Plugin.Logger.LogInfo("[FFA] Death observed on owner; server kill hook controls respawn scheduling.");
    }
}