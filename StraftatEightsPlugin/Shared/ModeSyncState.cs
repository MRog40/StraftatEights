using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

internal sealed class ModeSyncState
{
    private readonly float _settingsPushInterval;
    private readonly float _livePushInterval;
    private float _nextSettingsPushTime;
    private float _nextLivePushTime;

    internal int SettingsRevision { get; private set; }
    internal int LiveRevision { get; private set; }
    internal int LastLiveRoundId { get; private set; } = -1;

    internal ModeSyncState(float settingsPushInterval = 3f, float livePushInterval = 3f)
    {
        _settingsPushInterval = settingsPushInterval;
        _livePushInterval = livePushInterval;
    }

    internal int NextSettingsRevision() => ++SettingsRevision;

    internal int NextLiveRevision() => ++LiveRevision;

    internal bool IsSettingsPushDue()
    {
        return HostSettingsSync.IsDue(ref _nextSettingsPushTime, _settingsPushInterval);
    }

    internal bool IsLivePushDue()
    {
        return HostSettingsSync.IsDue(ref _nextLivePushTime, _livePushInterval);
    }

    internal bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastSettingsRoundId, ref _lastSettingsRevision);
    }

    internal bool TryAcceptLiveSnapshot(CSteamID hostId, int roundId, int revision)
    {
        bool accepted = SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastLiveRoundId, ref _lastLiveRevision);
        if (accepted)
        {
            LastLiveRoundId = roundId;
        }
        return accepted;
    }

    internal void ResetForLobby()
    {
        _lastSettingsRoundId = -1;
        _lastSettingsRevision = -1;
        _lastLiveRoundId = -1;
        _lastLiveRevision = -1;
        LastLiveRoundId = -1;
        _nextSettingsPushTime = 0f;
        _nextLivePushTime = 0f;
    }

    internal void ResetLiveState()
    {
        NextLiveRevision();
        _lastLiveRoundId = -1;
        _lastLiveRevision = -1;
        LastLiveRoundId = -1;
    }

    private int _lastSettingsRoundId = -1;
    private int _lastSettingsRevision = -1;
    private int _lastLiveRoundId = -1;
    private int _lastLiveRevision = -1;
}
