using System.Text;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class CaptureTheFlagHud
{
    internal static string BuildScoreboard()
    {
        StringBuilder text = new();
        text.Append(GameModeManager.GetModeLabelMarkup(GameMode.CaptureTheFlag))
            .Append("  ")
            .Append(Mathf.CeilToInt(CaptureTheFlagState.MatchTimeRemaining))
            .Append("s")
            .Append("\n")
            .Append("First to ")
            .Append(GameModeManager.EffectivePointsToWin)
            .Append("  |  ")
            .Append(CaptureTheFlagState.IsSuddenDeath ? "SUDDEN DEATH" : "LIVE");

        for (int teamId = 0; teamId < 2; teamId++)
        {
            TeamColorData teamColor = TeamRules.GetColor(teamId);
            string color = ColorUtility.ToHtmlStringRGB(new Color32(teamColor.Red,
                teamColor.Green, teamColor.Blue, 255));
            string teamName = teamId == TeamRules.BlueTeamId ? "BLUE" : "VERMILLION";
            text.Append("\n<color=#").Append(color).Append("><b>")
                .Append(teamName).Append("</b></color>  ")
                .Append(CaptureTheFlagState.GetScore(teamId)).Append("/")
                .Append(GameModeManager.EffectivePointsToWin);

            int flagIndex = FindFlagForTeam(teamId);
            if (flagIndex >= 0)
            {
                text.Append("  ").Append(GetFlagStatus(flagIndex));
            }
        }

        return text.ToString();
    }

    private static int FindFlagForTeam(int teamId)
    {
        for (int flagIndex = 0; flagIndex < 2; flagIndex++)
        {
            if (CaptureTheFlagState.GetFlagTeam(flagIndex) == teamId)
            {
                return flagIndex;
            }
        }

        return -1;
    }

    private static string GetFlagStatus(int flagIndex)
    {
        return CaptureTheFlagState.GetFlagStatus(flagIndex) switch
        {
            CaptureTheFlagFlagStatus.Home => "HOME",
            CaptureTheFlagFlagStatus.Dropped => "DROPPED",
            CaptureTheFlagFlagStatus.Carried => "CARRIED "
                + GetCarrierName(CaptureTheFlagState.GetFlagCarrier(flagIndex)),
            _ => "UNKNOWN"
        };
    }

    private static string GetCarrierName(int playerId)
    {
        if (playerId < 0)
        {
            return "-";
        }

        string name = ClientInstance.ReplaceAllPlayerNameTags(PlayerLookup.GetPlayerNameTag(playerId));
        return name.Length > 14 ? name.Substring(0, 14) : name;
    }
}
