using MyceliumNetworking;

namespace Eights;

public partial class Plugin
{
    [CustomRPC]
    public void SyncRespawnProtection(int playerId, bool pending, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        RespawnProtection.SetPendingRespawn(playerId, pending);
    }
}