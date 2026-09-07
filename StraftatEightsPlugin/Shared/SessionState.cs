using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

internal static class SessionState
{
    private static bool active;

    internal static int Generation { get; private set; }
    internal static bool IsActive => active && MyceliumNetwork.InLobby;

    internal static void BeginLobby()
    {
        if (active && MyceliumNetwork.InLobby)
        {
            return;
        }

        active = true;
        Generation++;
    }

    internal static void EndLobby()
    {
        active = false;
        Generation++;
    }

    internal static bool IsCurrent(int generation)
    {
        return IsActive && generation == Generation;
    }

    internal static bool TryAcceptSnapshot(int roundId, int revision,
        ref int lastRoundId, ref int lastRevision)
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

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision,
        ref int lastRoundId, ref int lastRevision)
    {
        if (!MyceliumNetwork.InLobby || hostId.m_SteamID == 0
            || MyceliumNetwork.LobbyHost.m_SteamID != hostId.m_SteamID)
        {
            return false;
        }

        return TryAcceptSnapshot(roundId, revision, ref lastRoundId, ref lastRevision);
    }
}