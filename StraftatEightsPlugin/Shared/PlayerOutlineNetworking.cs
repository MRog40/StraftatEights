using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    [CustomRPC]
    public void SyncOutlineRoleState(CSteamID hostId, int mode, int playerId,
        int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        PlayerOutline.ApplyRoleSnapshot(hostId, mode, playerId, roundId, revision);
    }
}