using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StraftatEightsPlugin;

// Cross-game-mode helpers for resolving player identity from PlayerHealth/ClientInstance. Shared by
// any mode that needs to know "who is this" or "who killed who" (Juggernaut today, future modes
// like Gun Game later) instead of every feature re-implementing the same lookups.
internal static class PlayerLookup
{
    internal static List<int> GetConnectedPlayerIds()
    {
        try
        {
            HashSet<int> playerIds = new();
            foreach (KeyValuePair<int, ClientInstance> entry in ClientInstance.playerInstances)
            {
                if (entry.Value != null && entry.Value)
                {
                    playerIds.Add(entry.Key);
                }
            }

            foreach (ClientInstance client in Object.FindObjectsOfType<ClientInstance>())
            {
                if (client != null && client && client.PlayerId >= 0)
                {
                    playerIds.Add(client.PlayerId);
                }
            }

            List<int> result = playerIds.ToList();
            result.Sort();
            return result;
        }
        catch
        {
            return new List<int>();
        }
    }

    internal static PlayerHealth? FindPlayerHealthById(int playerId)
    {
        if (playerId < 0)
        {
            return null;
        }
        if (ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client) && client != null)
        {
            if (client.PlayerSpawner != null && client.PlayerSpawner.player != null)
            {
                PlayerHealth? health = client.PlayerSpawner.player.GetComponent<PlayerHealth>();
                if (IsPlayerHealthForId(health, playerId))
                {
                    return health;
                }
                if (IsMappedLivePlayerHealth(health, client))
                {
                    return health;
                }
            }
            PlayerHealth? clientHealth = client.GetComponent<PlayerHealth>();
            if (IsPlayerHealthForId(clientHealth, playerId))
            {
                return clientHealth;
            }
        }

        foreach (PlayerHealth health in Object.FindObjectsOfType<PlayerHealth>(true))
        {
            if (IsPlayerHealthForId(health, playerId))
            {
                return health;
            }
        }

        foreach (ClientInstance sceneClient in Object.FindObjectsOfType<ClientInstance>())
        {
            if (sceneClient == null || !sceneClient || sceneClient.PlayerId != playerId)
            {
                continue;
            }

            PlayerManager? playerSpawner = sceneClient.PlayerSpawner;
            if (playerSpawner != null && playerSpawner.player != null)
            {
                PlayerHealth? health = playerSpawner.player.GetComponent<PlayerHealth>();
                if (IsPlayerHealthForId(health, playerId))
                {
                    return health;
                }
                if (IsMappedLivePlayerHealth(health, sceneClient))
                {
                    return health;
                }
            }

            PlayerHealth? sceneHealth = sceneClient.GetComponent<PlayerHealth>();
            if (IsPlayerHealthForId(sceneHealth, playerId))
            {
                return sceneHealth;
            }
        }
        return null;
    }

    internal static PlayerHealth? FindActivePlayerHealthById(int playerId)
    {
        if (playerId < 0)
        {
            return null;
        }

        foreach (PlayerHealth health in Object.FindObjectsOfType<PlayerHealth>())
        {
            if (IsPlayerHealthForId(health, playerId))
            {
                return health;
            }
        }

        return null;
    }

    private static bool IsPlayerHealthForId(PlayerHealth? health, int playerId)
    {
        return health != null && health.playerValues?.playerClient?.PlayerId == playerId;
    }

    private static bool IsMappedLivePlayerHealth(PlayerHealth? health, ClientInstance client)
    {
        return health != null && health && health.gameObject.activeInHierarchy
            && client.PlayerSpawner != null && client.PlayerSpawner.player != null
            && client.PlayerSpawner.player.GetComponent<PlayerHealth>() == health;
    }

    // Resolves a killer's PlayerId from a dead player's PlayerHealth.killer transform - every weapon
    // script (Gun, Shotgun, BeamGun, MeleeWeapon, etc) sets `enemyHealth.killer = rootObject.transform`
    // on a hit that kills, where rootObject is the attacker's player root.
    internal static int FindKillerId(PlayerHealth? deadPlayerHealth)
    {
        if (deadPlayerHealth == null || deadPlayerHealth.killer == null)
        {
            return -1;
        }
        PlayerHealth killerHealth = deadPlayerHealth.killer.GetComponentInParent<PlayerHealth>();
        if (killerHealth != null && killerHealth.playerValues != null && killerHealth.playerValues.playerClient != null)
        {
            return killerHealth.playerValues.playerClient.PlayerId;
        }

        ClientInstance killerClient = deadPlayerHealth.killer.GetComponentInParent<ClientInstance>();
        return killerClient != null ? killerClient.PlayerId : -1;
    }

    // Returns the {PLAYER_NAME}:{id} template tag - pass the final built string through
    // ClientInstance.ReplaceAllPlayerNameTags before displaying it.
    internal static string GetPlayerNameTag(int playerId)
    {
        return $"{{PLAYER_NAME}}:{{{playerId}}}";
    }

    // Straftat teams: ScoreManager.Instance.GetTeamId(playerId) returns a team id (0/1) when
    // SteamLobby.Instance.playingTeams is on. Future team-aware modes should read that directly
    // rather than duplicating team state here - there's no team info to cache, it's already live.
}
