using System;

namespace Eights;

internal static class CountertatRules
{
    private const int TeamZeroId = 0;
    private const int TeamOneId = 1;

    private static readonly string[][] TakeOneWeapons =
    {
        new[] { "Silenzzio" },
        new[] { "Glock" }
    };
    private static readonly string[][] TakeTwoWeapons =
    {
        new[] { "QCW05", "AR15", "SMG" },
        new[] { "AK-K", "Dispenser", "Mac10" }
    };
    private static readonly string[][] TakeThreeWeapons =
    {
        new[] { "QCW05", "QCW05", "AR15" },
        new[] { "AK-K", "AK-K", "AK-K", "Dispenser" }
    };
    private static readonly string[][] LaterTakeWeapons =
    {
        new[] { "QCW05", "M2000", "QCW05" },
        new[] { "AK-K", "M2000", "AK-K" }
    };

    internal static int GetAboubiTeamId(int roundId)
    {
        return (roundId & 1) == 0 ? TeamZeroId : TeamOneId;
    }

    internal static int GetShadowForceTeamId(int roundId)
    {
        return GetAboubiTeamId(roundId) == TeamZeroId ? TeamOneId : TeamZeroId;
    }

    internal static int GetOffensiveTeamId(int roundId)
    {
        return GetAboubiTeamId(roundId);
    }

    internal static string GetTeamName(int teamId, int aboubiTeamId)
    {
        if (teamId == aboubiTeamId)
        {
            return "Aboubi";
        }
        if (teamId == (aboubiTeamId == TeamZeroId ? TeamOneId : TeamZeroId))
        {
            return "Shadow Force";
        }

        return "Unknown Team";
    }

    internal static string GetWeaponName(int aboubiTeamId, int takeId, int teamId,
        int playerSlot)
    {
        if (takeId < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(takeId));
        }
        if (teamId != TeamZeroId && teamId != TeamOneId)
        {
            throw new ArgumentOutOfRangeException(nameof(teamId));
        }
        if (playerSlot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerSlot));
        }

        string[][] teamWeapons = takeId switch
        {
            1 => TakeOneWeapons,
            2 => TakeTwoWeapons,
            3 => TakeThreeWeapons,
            _ => LaterTakeWeapons
        };
        int weaponSequenceIndex = teamId == aboubiTeamId ? 1 : 0;
        string[] weapons = teamWeapons[weaponSequenceIndex];
        return weapons[Math.Min(playerSlot, weapons.Length - 1)];
    }
}