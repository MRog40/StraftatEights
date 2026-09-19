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

        __result = TeamAssignment.ResolveTeamId(playerId);
        return false;
    }
}

[HarmonyPatch(typeof(GameManager), "Update")]
internal static class GameManager_CustomTeamState_Patch
{
    private static void Postfix(GameManager __instance)
    {
        if (!GameModeManager.IsCustomMode || !__instance.IsServer || !__instance.playingTeams)
        {
            return;
        }

        __instance.sync___set_value_playingTeams(false, true);
    }
}