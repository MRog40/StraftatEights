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
            List<int> playerIds = new();
            foreach (KeyValuePair<int, ClientInstance> entry in ClientInstance.playerInstances)
            {
                if (entry.Value != null && entry.Value)
                {
                    playerIds.Add(entry.Key);
                }
            }

            if (playerIds.Count == 0)
            {
                playerIds = Object.FindObjectsOfType<ClientInstance>()
                    .Where(client => client != null && client && client.PlayerId >= 0)
                    .Select(client => client.PlayerId)
                    .Distinct()
                    .ToList();
            }

            playerIds.Sort();
            return playerIds;
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
                return client.PlayerSpawner.player.GetComponent<PlayerHealth>();
            }
            return client.GetComponent<PlayerHealth>();
        }
        return null;
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
