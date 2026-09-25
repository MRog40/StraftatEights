using UnityEngine;

namespace Eights;

internal static class TeamColorPolicy
{
    internal static Color GetRelativeTeamColor(int teamId, float alpha = 1f)
    {
        TeamColorData color = GetRelativeTeamColorData(teamId);
        return new Color(color.Red / 255f, color.Green / 255f, color.Blue / 255f,
            Mathf.Clamp01(alpha));
    }

    internal static TeamColorData GetRelativeTeamColorData(int teamId)
    {
        TeamColorData color = TeamRules.GetColor(teamId);
        if (TryGetLocalTeamId(out int localTeamId))
        {
            color = teamId == localTeamId
                ? TeamRules.GetColor(TeamRules.BlueTeamId)
                : TeamRules.GetColor(TeamRules.VermillionTeamId);
        }

            return color;
    }

    private static bool TryGetLocalTeamId(out int teamId)
    {
        int localPlayerId = ClientInstance.Instance == null ? -1 : ClientInstance.Instance.PlayerId;
        return TeamAssignment.TryGetTeamId(localPlayerId, out teamId);
    }
}