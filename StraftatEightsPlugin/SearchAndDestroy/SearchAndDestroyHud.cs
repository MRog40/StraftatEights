using System.Text;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyHud
{
    internal static string BuildScoreboard()
    {
        StringBuilder text = new();
        text.Append(GameModeManager.GetModeLabelMarkup(GameMode.SearchAndDestroy))
            .Append("  ")
            .Append(Mathf.CeilToInt(SearchAndDestroyState.SubRoundTimeRemaining))
            .Append("s\nFirst to ")
            .Append(GameModeManager.EffectivePointsToWin)
            .Append("  |  LIVE");

        for (int teamId = 0; teamId < 2; teamId++)
        {
            TeamColorData colorData = TeamRules.GetColor(teamId);
            string color = ColorUtility.ToHtmlStringRGB(new Color32(colorData.Red,
                colorData.Green, colorData.Blue, 255));
            text.Append("\n<color=#").Append(color).Append("><b>")
                .Append("TEAM ").Append(teamId + 1).Append("</b></color>: ")
                .Append(SearchAndDestroyState.GetScore(teamId)).Append("/")
                .Append(GameModeManager.EffectivePointsToWin);
        }

        text.Append("\n").Append(GetBombStatus());
        return text.ToString();
    }

    private static string GetBombStatus()
    {
        return SearchAndDestroyState.BombStatus switch
        {
            SearchAndDestroyBombStatus.Home => "BOMB: HOME",
            SearchAndDestroyBombStatus.Carried => "BOMB: CARRIED",
            SearchAndDestroyBombStatus.Dropped => "BOMB: DROPPED",
            SearchAndDestroyBombStatus.Planted => GetPlantedStatus(),
            _ => "BOMB: UNKNOWN"
        };
    }

    private static string GetPlantedStatus()
    {
        string site = SearchAndDestroyState.BombSiteIndex == 0 ? "CIRCLE"
            : SearchAndDestroyState.BombSiteIndex == 1 ? "DIAMOND" : "?";
        int seconds = Mathf.CeilToInt(SearchAndDestroyState.FuseTimeRemaining);
        if (SearchAndDestroyState.DefuserPlayerId >= 0)
        {
            int percent = Mathf.RoundToInt(SearchAndDestroyState.DefuseProgress
                / SearchAndDestroyState.DefuseDurationSeconds * 100f);
            return $"BOMB: {site}  DEFUSING {percent}%  {seconds}s";
        }

        return $"BOMB: {site}  PLANTED  {seconds}s";
    }
}
