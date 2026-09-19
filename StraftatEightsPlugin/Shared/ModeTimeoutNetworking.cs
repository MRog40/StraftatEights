using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    [CustomRPC]
    public void SyncModeTimeout(CSteamID hostId, float timeRemaining, bool suddenDeath,
        int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        ModeTimeoutState.ApplyLiveState(hostId, timeRemaining, suddenDeath, roundId, revision);
    }
}