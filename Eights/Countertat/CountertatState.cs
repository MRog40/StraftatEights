using System.Collections.Generic;
using System.Globalization;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

internal static class CountertatState
{
    internal const string SettingsLobbyDataKey = "Eights_Countertat_Settings";

    private static readonly ModeSyncState Sync = new();
    private static int _officialRoundNumber;
    private static int _lastStartedGameModeRoundId = -1;

    internal static bool Enabled { get; private set; }
    internal static int AboubiTeamId => CountertatRules.GetAboubiTeamId(_officialRoundNumber);

    internal static void OnRoundStarted(int gameModeRoundId)
    {
        if (!MyceliumNetwork.IsHost || !GameModeManager.IsActive(GameMode.Countertat)
            || gameModeRoundId == _lastStartedGameModeRoundId)
        {
            return;
        }

        _lastStartedGameModeRoundId = gameModeRoundId;
        _officialRoundNumber++;
    }

    internal static void ApplySettings(bool enabled)
    {
        if (GameModeManager.ShouldDeferModeDisable(GameMode.Countertat, enabled))
        {
            return;
        }

        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed && GameModeManager.IsActive(GameMode.Countertat))
        {
            SndtatState.ResetMatchState();
        }
    }

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettings(Plugin.CountertatEnabled.Value);
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision,
            Plugin.CountertatEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.CountertatModId,
            nameof(Plugin.SyncCountertatSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.CountertatEnabled.Value);
    }

    internal static void PeriodicPushSettingsIfHost()
    {
        if (Sync.IsSettingsPushDue())
        {
            PushSettingsIfHost();
        }
    }

    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        ResetRoundRotation();
        if (MyceliumNetwork.IsHost)
        {
            PushSettingsIfHost();
        }
        else
        {
            ApplyLobbySettingsSnapshot();
        }
    }

    internal static void OnLobbyLeft()
    {
        Sync.ResetForLobby();
        Enabled = false;
        ResetRoundRotation();
    }

    private static void ResetRoundRotation()
    {
        _officialRoundNumber = 0;
        _lastStartedGameModeRoundId = -1;
    }

    internal static void OnLobbyDataUpdated(List<string> keys)
    {
        if (!MyceliumNetwork.IsHost && MyceliumNetwork.InLobby
            && ModeLobbyDataSync.ContainsKey(keys, SettingsLobbyDataKey))
        {
            ApplyLobbySettingsSnapshot();
        }
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.CountertatModId,
            nameof(Plugin.SyncCountertatSettings), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.CountertatEnabled.Value);
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId,
        int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("countertat", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }
}