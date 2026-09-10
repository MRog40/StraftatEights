using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

internal static class SessionState
{
    private static bool active;
    private static string? lastRejectedSnapshot;

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
        lastRejectedSnapshot = null;
    }

    internal static void EndLobby()
    {
        active = false;
        Generation++;
        lastRejectedSnapshot = null;
    }

    internal static bool IsCurrent(int generation)
    {
        return IsActive && generation == Generation;
    }

    internal static bool TryAcceptSnapshot(int roundId, int revision,
        ref int lastRoundId, ref int lastRevision)
    {
        return SnapshotValidation.TryAccept(roundId, revision, ref lastRoundId, ref lastRevision);
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision,
        ref int lastRoundId, ref int lastRevision, string source = "unknown")
    {
        bool inLobby = MyceliumNetwork.InLobby;
        ulong lobbyHostId = inLobby ? MyceliumNetwork.LobbyHost.m_SteamID : 0;
        if (!inLobby)
        {
            LogRejectedSnapshot(source, "not-in-lobby", hostId.m_SteamID, lobbyHostId,
                roundId, revision, lastRoundId, lastRevision);
            return false;
        }

        if (hostId.m_SteamID == 0 || lobbyHostId != hostId.m_SteamID)
        {
            LogRejectedSnapshot(source, "host-mismatch", hostId.m_SteamID, lobbyHostId,
                roundId, revision, lastRoundId, lastRevision);
            return false;
        }

        if (!TryAcceptSnapshot(roundId, revision, ref lastRoundId, ref lastRevision))
        {
            string reason = roundId < 0 || revision < 0 ? "invalid-cursor" : "stale-cursor";
            LogRejectedSnapshot(source, reason, hostId.m_SteamID, lobbyHostId,
                roundId, revision, lastRoundId, lastRevision);
            return false;
        }

        return true;
    }

    private static void LogRejectedSnapshot(string source, string reason, ulong hostId,
        ulong lobbyHostId, int roundId, int revision, int lastRoundId, int lastRevision)
    {
        string signature = string.Join("|", source, reason, hostId, lobbyHostId,
            roundId, revision, lastRoundId, lastRevision);
        if (signature == lastRejectedSnapshot)
        {
            return;
        }

        lastRejectedSnapshot = signature;
        Plugin.Logger?.LogWarning($"[Sync] Rejected snapshot source={source} reason={reason} "
            + $"host={hostId} lobbyHost={lobbyHostId} round={roundId} revision={revision} "
            + $"lastRound={lastRoundId} lastRevision={lastRevision}");
    }
}