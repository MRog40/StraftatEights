using System;
using System.Collections.Generic;
using System.Linq;

namespace Eights;

internal static class SnapshotValidation
{
    internal static bool TryAccept(int roundId, int revision, ref int lastRoundId, ref int lastRevision)
    {
        if (roundId < 0 || revision < 0
            || roundId < lastRoundId || (roundId == lastRoundId && revision < lastRevision))
        {
            return false;
        }

        lastRoundId = roundId;
        lastRevision = revision;
        return true;
    }
}

internal static class GameModeToggleRules
{
    internal static bool ShouldEnableAll(IEnumerable<bool> enabledModes)
    {
        return enabledModes.Any(enabled => !enabled);
    }
}

internal static class WeaponListParser
{
    internal static List<string> Parse(string value, IEnumerable<string> validNames)
    {
        HashSet<string> valid = new(validNames, StringComparer.Ordinal);
        return (value ?? string.Empty).Split(',', ';')
            .Select(item => item.Trim())
            .Where(item => item.Length > 0 && valid.Contains(item))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}

internal static class DefaultKnifeRules
{
    internal const string DefaultWeaponName = "Couperet";

    internal static bool ShouldProvideKnife(bool settingEnabled, bool hasGun)
    {
        return settingEnabled && !hasGun;
    }

    internal static bool IsDefaultKnife(string weaponName)
    {
        return weaponName.StartsWith(DefaultWeaponName, StringComparison.Ordinal);
    }

    internal static bool IsKnife(string weaponName)
    {
        return IsDefaultKnife(weaponName)
            || weaponName.StartsWith("Impetus", StringComparison.Ordinal);
    }
}

internal static class FootstepAudioRules
{
    internal static bool ShouldPlay(int clip, bool silentWalking, bool sprinting)
    {
        return !IsFootstepClip(clip) || !silentWalking || sprinting;
    }

    internal static bool IsFootstepClip(int clip)
    {
        return clip is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9
            or 12 or 13 or 14 or 15 or 16 or 19 or 20;
    }
}

internal static class GuntatRules
{
    internal static int GetWeaponIndex(int progress, int weaponCount)
    {
        if (weaponCount <= 1 || progress <= 0)
        {
            return 0;
        }

        int kills = progress / ScoreRules.PointsPerKill;
        return Math.Min(kills, weaponCount - 1);
    }
}

internal static class ScoreCodec
{
    internal static string Serialize(IReadOnlyDictionary<int, int> scores)
    {
        return string.Join(";", scores.Select(entry => $"{entry.Key}:{entry.Value}"));
    }

    internal static Dictionary<int, int> Parse(string data, int maximumScore)
    {
        Dictionary<int, int> scores = new();
        foreach (string entry in (data ?? string.Empty).Split(';'))
        {
            int separator = entry.IndexOf(':');
            if (separator > 0 && int.TryParse(entry.Substring(0, separator), out int id)
                && int.TryParse(entry.Substring(separator + 1), out int score)
                && id >= 0 && score >= 0 && score <= maximumScore)
            {
                scores[id] = score;
            }
        }
        return scores;
    }

}

internal static class HealthUnits
{
    internal const float DisplayedHealthPerInternalUnit = 25f;

    internal static float ToInternal(float displayedHealth)
    {
        return displayedHealth / DisplayedHealthPerInternalUnit;
    }
}

internal static class ChambertatRules
{
    internal const float PlayerHealth = 0.4f;

    internal static bool IsAllowedWeapon(string weaponName)
    {
        return weaponName.StartsWith("Revolver", StringComparison.Ordinal)
            || weaponName.StartsWith("Couperet", StringComparison.Ordinal);
    }

    internal static (int Magazine, int Reserve) AwardBullet(int magazine, int reserve)
    {
        int totalRounds = Math.Max(0, magazine) + Math.Max(0, reserve) + 1;
        return (totalRounds, 0);
    }

    internal static bool ApplyDeath(HashSet<int> alivePlayers, int deadPlayerId, int killerId)
    {
        if (!alivePlayers.Remove(deadPlayerId))
        {
            return false;
        }

        return true;
    }
}

internal static class PotatotatRules
{
    internal const string PotatoWeaponName = "HandGrenade";
    internal const int PointsPerKill = 20;
    internal const int PointsToWin = 100;
    internal static readonly string[] WeaponOrder =
    {
        "Elephant",
        "SmithCarbine",
        "Shotgun",
        "Gust",
        "Tromblonj"
    };

    internal static bool HasExpectedWeapon(bool hasExpectedRightHandWeapon,
        bool hasExpectedLeftHandWeapon)
    {
        return hasExpectedRightHandWeapon || hasExpectedLeftHandWeapon;
    }

    internal static int AddKillPoints(int currentScore)
    {
        return ScoreRules.AddPoints(currentScore, PointsPerKill, PointsToWin);
    }

    internal static string GetWeaponForScore(int score)
    {
        int weaponIndex = Math.Min(Math.Max(0, score) / PointsPerKill,
            WeaponOrder.Length - 1);
        return WeaponOrder[weaponIndex];
    }

    internal static int ResetKillStreakOnGrenadeDeath(IDictionary<int, int> kills, int playerId)
    {
        if (playerId < 0 || !kills.TryGetValue(playerId, out int currentKills))
        {
            return 0;
        }

        kills[playerId] = 0;
        return currentKills;
    }

    internal static bool IsGrenadeDeath(int killerId, int potatoPlayerId,
        int recentGrenadeSourcePlayerId, bool grenadeDeathMarked)
    {
        return grenadeDeathMarked
            || (potatoPlayerId >= 0 && killerId == potatoPlayerId)
            || (recentGrenadeSourcePlayerId >= 0
                && killerId == recentGrenadeSourcePlayerId);
    }

    internal static bool IsAllowedWeapon(string weaponName, bool hasPotato, int score)
    {
        if (hasPotato)
        {
            return weaponName.StartsWith(PotatoWeaponName, StringComparison.Ordinal);
        }

        return weaponName.StartsWith(GetWeaponForScore(score), StringComparison.Ordinal);
    }

    internal static int ResolvePotato(int potatoPlayerId, int killerId, int deadPlayerId)
    {
        if (potatoPlayerId < 0 && deadPlayerId >= 0)
        {
            return deadPlayerId;
        }

        return killerId == potatoPlayerId && killerId != deadPlayerId
            ? deadPlayerId
            : potatoPlayerId;
    }
}

internal static class InfideltatRules
{
    internal const int PointsForKillingInfideltat = 30;
    internal const int PointsForInfideltatWin = 50;

    internal static int GetKillerAward(bool deadWasInfideltat, bool killerIsInfideltat)
    {
        return deadWasInfideltat && !killerIsInfideltat ? PointsForKillingInfideltat : 0;
    }

    internal static int GetWinnerAward(bool infidelWon)
    {
        return infidelWon ? PointsForInfideltatWin : 0;
    }
}

internal static class InfectedtatRules
{
    internal const string KnifeWeaponName = "Couperet";

    internal static bool CanEquipWeapon(bool playerIdentityKnown, bool infected,
        string weaponName)
    {
        // Unknown identity is not permission to equip during Infectedtat.
        return playerIdentityKnown && (!infected
            || weaponName.StartsWith(KnifeWeaponName, StringComparison.Ordinal));
    }

    internal static List<int> GetSurvivors(IEnumerable<int> roundPlayers,
        ISet<int> infectedPlayers)
    {
        return roundPlayers.Where(playerId => !infectedPlayers.Contains(playerId))
            .Distinct().OrderBy(playerId => playerId).ToList();
    }

    internal static bool ShouldBecomeInfectedtat(bool deadWasInfectedtat)
    {
        return !deadWasInfectedtat;
    }

    internal static bool ShouldEndRound(int survivorCount)
    {
        return survivorCount <= 0;
    }

    internal static bool ShouldAwardInitialInfectedtat(int initialInfectedtatPlayerId,
        int survivorCount)
    {
        return initialInfectedtatPlayerId >= 0 && ShouldEndRound(survivorCount);
    }
}

internal static class MichaeltatRules
{
    internal const float RoundTimeLimitSeconds = ModeTimeoutRules.DefaultRoundSeconds;
    internal const float HuntHealth = 10f;
    internal const float FinalBattleHealth = 100f;
    internal const float MovementMultiplier = 1.05f;

    internal static float GetHealth(bool finalBattle)
    {
        return finalBattle ? FinalBattleHealth : HuntHealth;
    }

    internal static bool ShouldEndTimeoutWithoutWinner(int alivePlayerCount)
    {
        return alivePlayerCount > 1;
    }
}

internal static class AssassintatRules
{
    internal const float DefaultTakeTimeLimitSeconds = ModeTimeoutRules.DefaultRoundSeconds;
    internal const int PointsForAssassintatWin = 50;
    internal const int PointsForKingSurvival = 30;
    internal const int PointsForBodyguardSurvival = 10;
    internal const int PointsForBodyguardKill = 20;

    internal static bool IsTerminalDeath(bool deadWasKing, bool deadWasAssassintat)
    {
        return deadWasKing || deadWasAssassintat;
    }

    internal static int GetAssassintatAward(bool deadWasKing)
    {
        return deadWasKing ? PointsForAssassintatWin : 0;
    }

    internal static int GetKingAward(bool deadWasAssassintat)
    {
        return deadWasAssassintat ? PointsForKingSurvival : 0;
    }

    internal static int GetBodyguardAward(bool deadWasAssassintat)
    {
        return deadWasAssassintat ? PointsForBodyguardSurvival : 0;
    }

    internal static int GetBodyguardKillerAward(bool deadWasAssassintat, bool killerIsBodyguard,
        bool killerIsVictim)
    {
        return deadWasAssassintat && killerIsBodyguard && !killerIsVictim
            ? PointsForBodyguardKill
            : 0;
    }
}

internal static class ModeCycle
{
    internal static bool TrySelectNext<T>(IReadOnlyList<T> modes, T current, out T next)
    {
        if (modes.Count == 0)
        {
            next = default!;
            return false;
        }

        int currentIndex = -1;
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int index = 0; index < modes.Count; index++)
        {
            if (comparer.Equals(modes[index], current))
            {
                currentIndex = index;
                break;
            }
        }

        next = modes[(currentIndex + 1) % modes.Count];
        return true;
    }

    internal static bool TrySelectRandom<T>(IReadOnlyList<T> modes, T current, int selectionIndex, out T next)
    {
        if (modes.Count == 0)
        {
            next = default!;
            return false;
        }

        int currentIndex = -1;
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int index = 0; index < modes.Count; index++)
        {
            if (comparer.Equals(modes[index], current))
            {
                currentIndex = index;
                break;
            }
        }

        int candidateCount = currentIndex < 0 ? modes.Count : modes.Count - 1;
        if (candidateCount == 0)
        {
            next = modes[0];
            return true;
        }

        int candidateIndex = (int)((uint)selectionIndex % (uint)candidateCount);
        if (currentIndex >= 0 && candidateIndex >= currentIndex)
        {
            candidateIndex++;
        }
        next = modes[candidateIndex];
        return true;
    }
}

internal sealed class RequestVersionTracker
{
    private readonly Dictionary<int, int> versions = new();

    internal int Next(int playerId)
    {
        int nextVersion = versions.TryGetValue(playerId, out int currentVersion)
            ? currentVersion + 1
            : 1;
        versions[playerId] = nextVersion;
        return nextVersion;
    }

    internal bool IsCurrent(int playerId, int version)
    {
        return versions.TryGetValue(playerId, out int currentVersion)
            && currentVersion == version;
    }

    internal void Clear()
    {
        versions.Clear();
    }
}
