using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

// Cross-game-mode helpers for resolving player identity from PlayerHealth/ClientInstance. Shared by
// any mode that needs to know "who is this" or "who killed who" (Juggernaut today, future modes
// like Gun Game later) instead of every feature re-implementing the same lookups.
internal static class PlayerLookup
{
    private static readonly List<int> CachedConnectedPlayerIds = new();
    private static readonly Dictionary<int, PlayerHealth> CachedPlayerHealthById = new();
    private static bool _connectedPlayerIdsDirty = true;
    private static int _cachedMappedPlayerCount = -1;

    internal static ICollection<PlayerHealth> KnownPlayerHealths => CachedPlayerHealthById.Values;
    internal static int ConnectedPlayerRevision { get; private set; }

    internal static void Initialize()
    {
        MyceliumNetwork.LobbyCreated += ResetCaches;
        MyceliumNetwork.LobbyEntered += ResetCaches;
        MyceliumNetwork.LobbyLeft += ResetCaches;
        MyceliumNetwork.PlayerEntered += InvalidateConnectedPlayerCache;
        MyceliumNetwork.PlayerLeft += OnPlayerLeft;
    }

    internal static List<int> GetConnectedPlayerIds()
    {
        RefreshConnectedPlayerCache();
        return new List<int>(CachedConnectedPlayerIds);
    }

    internal static IReadOnlyList<int> GetConnectedPlayerIdsReadOnly()
    {
        RefreshConnectedPlayerCache();
        return CachedConnectedPlayerIds;
    }

    private static void RefreshConnectedPlayerCache()
    {
        try
        {
            int mappedPlayerCount = ClientInstance.playerInstances.Count;
            if (!_connectedPlayerIdsDirty && _cachedMappedPlayerCount == mappedPlayerCount)
            {
                return;
            }

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

            bool membershipChanged = CachedConnectedPlayerIds.Count != playerIds.Count;
            if (!membershipChanged)
            {
                foreach (int cachedPlayerId in CachedConnectedPlayerIds)
                {
                    if (!playerIds.Contains(cachedPlayerId))
                    {
                        membershipChanged = true;
                        break;
                    }
                }
            }

            CachedConnectedPlayerIds.Clear();
            foreach (int playerId in playerIds)
            {
                CachedConnectedPlayerIds.Add(playerId);
            }
            CachedConnectedPlayerIds.Sort();
            if (membershipChanged)
            {
                ConnectedPlayerRevision++;
            }
            _cachedMappedPlayerCount = mappedPlayerCount;
            _connectedPlayerIdsDirty = false;
        }
        catch
        {
            CachedConnectedPlayerIds.Clear();
            _cachedMappedPlayerCount = -1;
            _connectedPlayerIdsDirty = true;
        }
    }

    private static void InvalidateConnectedPlayerCache()
    {
        _connectedPlayerIdsDirty = true;
        _cachedMappedPlayerCount = -1;
        ConnectedPlayerRevision++;
    }

    private static void InvalidateConnectedPlayerCache(CSteamID _)
    {
        InvalidateConnectedPlayerCache();
    }

    private static void OnPlayerLeft(CSteamID steamId)
    {
        InvalidateConnectedPlayerCache();
        CachedPlayerHealthById.Clear();
    }

    private static void ResetCaches()
    {
        CachedConnectedPlayerIds.Clear();
        CachedPlayerHealthById.Clear();
        _connectedPlayerIdsDirty = true;
        _cachedMappedPlayerCount = -1;
        ConnectedPlayerRevision++;
    }

    internal static void RegisterSpawnedPlayer(PlayerManager manager)
    {
        if (manager == null || !manager || manager.player == null || !manager.player)
        {
            return;
        }

        PlayerHealth? health = manager.player.GetComponent<PlayerHealth>();
        if (health == null || !health)
        {
            return;
        }

        ClientInstance? client = manager.GetComponent<ClientInstance>();
        int playerId = client != null && client
            ? client.PlayerId
            : health.playerValues?.playerClient?.PlayerId ?? -1;
        if (playerId >= 0)
        {
            CachedPlayerHealthById[playerId] = health;
        }
    }

    internal static PlayerHealth? FindPlayerHealthById(int playerId)
    {
        if (playerId < 0)
        {
            return null;
        }

        if (CachedPlayerHealthById.TryGetValue(playerId, out PlayerHealth? cachedHealth))
        {
            if (cachedHealth != null && IsPlayerHealthForId(cachedHealth, playerId))
            {
                return cachedHealth;
            }

            CachedPlayerHealthById.Remove(playerId);
        }

        if (ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client) && client != null)
        {
            if (client.PlayerSpawner != null && client.PlayerSpawner.player != null)
            {
                PlayerHealth? health = client.PlayerSpawner.player.GetComponent<PlayerHealth>();
                if (IsPlayerHealthForId(health, playerId))
                {
                    CachedPlayerHealthById[playerId] = health;
                    return health;
                }
                if (IsMappedLivePlayerHealth(health, client))
                {
                    CachedPlayerHealthById[playerId] = health;
                    return health;
                }
            }
            PlayerHealth? clientHealth = client.GetComponent<PlayerHealth>();
            if (IsPlayerHealthForId(clientHealth, playerId))
            {
                CachedPlayerHealthById[playerId] = clientHealth;
                return clientHealth;
            }
        }
        return null;
    }

    internal static int FindPlayerId(CSteamID steamId)
    {
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client && client.PlayerSteamID == steamId.m_SteamID)
            {
                return client.PlayerId;
            }
        }

        return -1;
    }

    internal static PlayerHealth? FindActivePlayerHealthById(int playerId)
    {
        if (playerId < 0)
        {
            return null;
        }

        if (CachedPlayerHealthById.TryGetValue(playerId, out PlayerHealth? cachedHealth))
        {
            if (cachedHealth != null && cachedHealth
                && IsPlayerHealthForId(cachedHealth, playerId)
                && cachedHealth.gameObject.activeInHierarchy)
            {
                return cachedHealth;
            }

            CachedPlayerHealthById.Remove(playerId);
        }

        if (ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client)
            && client != null && client)
        {
            if (client.PlayerSpawner != null && client.PlayerSpawner
                && client.PlayerSpawner.player != null && client.PlayerSpawner.player)
            {
                PlayerHealth? health = client.PlayerSpawner.player.GetComponent<PlayerHealth>();
                if (IsPlayerHealthForId(health, playerId) && health.gameObject.activeInHierarchy)
                {
                    CachedPlayerHealthById[playerId] = health;
                    return health;
                }
                if (IsMappedLivePlayerHealth(health, client))
                {
                    CachedPlayerHealthById[playerId] = health;
                    return health;
                }
            }

            PlayerHealth? clientHealth = client.GetComponent<PlayerHealth>();
            if (IsPlayerHealthForId(clientHealth, playerId)
                && clientHealth.gameObject.activeInHierarchy)
            {
                CachedPlayerHealthById[playerId] = clientHealth;
                return clientHealth;
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
