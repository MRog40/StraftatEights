namespace StraftatEightsPlugin;

internal static class TeamDeathmatchHud
{
    internal static string BuildScoreboard()
    {
        GameModeScoreboardRow[] rows = new GameModeScoreboardRow[2];
        for (int teamId = 0; teamId < 2; teamId++)
        {
            rows[teamId] = new GameModeScoreboardRow("TEAM " + (teamId + 1),
                TeamDeathmatchState.GetScore(teamId));
        }

        return GameModeScoreboard.Build(GameMode.TeamDeathmatch, null,
            GameModeManager.EffectivePointsToWin, rows);
    }
}