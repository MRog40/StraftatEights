using MyceliumNetworking;

namespace StraftatEightsPlugin;

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