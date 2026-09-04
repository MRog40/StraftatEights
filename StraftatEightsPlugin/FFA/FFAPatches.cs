using HarmonyLib;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(GameManager), "PlayerDied")]
internal static class GameManager_FFAKill_Patch
{
    private static bool Prefix(GameManager __instance, int playerId)
    {
        if (!FFAState.Enabled || !__instance.IsServer)
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
        if (!FFAState.Enabled || !__instance.IsOwner)
        {
            return;
        }
        PlayerManager manager = __instance.GetComponent<PlayerManager>();
        if (manager != null)
        {
            GameModeRespawn.Schedule(manager, FFAState.RespawnDelaySeconds);
        }
    }
}