namespace Eights;

internal static class TeamDeathmatchRules
{
    internal const int PointsPerKill = ScoreRules.PointsPerKill;

    internal static int AddKillPoints(int currentScore, int pointsToWin)
    {
        return ScoreRules.AddPoints(currentScore, PointsPerKill, pointsToWin);
    }

    internal static bool IsMatchWon(int score, int pointsToWin)
    {
        return score >= pointsToWin;
    }
}