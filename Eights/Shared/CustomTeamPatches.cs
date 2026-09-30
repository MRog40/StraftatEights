using HarmonyLib;

namespace Eights;

[HarmonyPatch(typeof(ScoreManager), nameof(ScoreManager.GetTeamId))]
internal static class ScoreManager_CustomTeam_Patch
{
    private static bool Prefix(int playerId, ref int __result)
    {
        if (!GameModeManager.IsCustomMode)
        {
            return true;
        }

        __result = GameModeManager.IsTeamBased
            ? TeamAssignment.ResolveTeamId(playerId)
            : playerId;
        return false;
    }
}

[HarmonyPatch(typeof(GameManager), "Update")]
internal static class GameManager_CustomTeamState_Patch
{
    private static void Postfix(GameManager __instance)
    {
        if (!GameModeManager.IsCustomMode || !__instance.playingTeams)
        {
            return;
        }

        if (__instance.IsServer)
        {
            __instance.sync___set_value_playingTeams(false, true);
        }
        else
        {
            __instance.playingTeams = false;
        }
    }
}