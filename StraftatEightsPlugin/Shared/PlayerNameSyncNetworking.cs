using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    [CustomRPC]
    public void SyncPlayerDisplayNames(CSteamID hostId, int roundId, int revision,
        string payload, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        PlayerNameSync.ApplySnapshot(hostId, roundId, revision, payload, "rpc");
    }
}