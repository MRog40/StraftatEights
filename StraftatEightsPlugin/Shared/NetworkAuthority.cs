using MyceliumNetworking;

namespace StraftatEightsPlugin;

internal static class NetworkAuthority
{
    internal static bool IsHostSender(RPCInfo info)
    {
        return MyceliumNetwork.InLobby
            && info.SenderSteamID.m_SteamID != 0
            && info.SenderSteamID.m_SteamID == MyceliumNetwork.LobbyHost.m_SteamID;
    }

    internal static bool IsPlayerSender(RPCInfo info, int playerId)
    {
        if (!MyceliumNetwork.InLobby || playerId < 0 || info.SenderSteamID.m_SteamID == 0)
        {
            return false;
        }

        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client && client.PlayerId == playerId
                && client.PlayerSteamID == info.SenderSteamID.m_SteamID)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsLocalPlayer(int playerId)
    {
        return playerId >= 0 && ClientInstance.Instance != null
            && ClientInstance.Instance.PlayerId == playerId;
    }
}
