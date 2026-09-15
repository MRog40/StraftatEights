using System.Text;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyHud
{
    internal static string BuildScoreboard()
    {
        StringBuilder text = new();
        text.Append(GameModeManager.GetModeLabelMarkup(GameMode.SearchAndDestroy))
            .Append("  Round ")
            .Append(SearchAndDestroyState.SubRoundId)
            .Append("  First to ")
            .Append(GameModeManager.EffectivePointsToWin);

        int localPlayerId = ClientInstance.Instance?.PlayerId ?? -1;
        string role = SearchAndDestroyState.IsOffensePlayer(localPlayerId)
            ? "OFFENSE"
            : SearchAndDestroyState.IsDefensePlayer(localPlayerId) ? "DEFENSE" : "SPECTATOR";
        text.Append("\nRole: ").Append(role);

        for (int teamId = 0; teamId < 2; teamId++)
        {
            TeamColorData colorData = teamId == SearchAndDestroyState.OffensiveTeamId
                ? new TeamColorData(220, 45, 45)
                : new TeamColorData(45, 110, 235);
            string color = ColorUtility.ToHtmlStringRGB(new Color32(colorData.Red,
                colorData.Green, colorData.Blue, 255));
            string teamName = teamId == SearchAndDestroyState.OffensiveTeamId
                ? "OFFENSE"
                : "DEFENSE";
            text.Append("\n<color=#").Append(color).Append("><b>")
                .Append(teamName).Append("</b></color>  ")
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
        string site = SearchAndDestroyState.BombSiteIndex == 0 ? "A"
            : SearchAndDestroyState.BombSiteIndex == 1 ? "B" : "?";
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
