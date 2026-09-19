namespace Eights;

internal static class TeamDeathmatchRules
{
    internal const int PointsPerKill = ScoreRules.PointsPerKill;

    internal static int AddKillPoints(int currentScore, int pointsToWin)
    {
        return System.Math.Min(pointsToWin, currentScore + PointsPerKill);
    }

    internal static bool IsMatchWon(int score, int pointsToWin)
    {
        return score >= pointsToWin;
    }
}