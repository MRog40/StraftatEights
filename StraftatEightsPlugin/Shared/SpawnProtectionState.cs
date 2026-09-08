using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class SpawnProtectionState
{
    private const float BroadcastInterval = 0.5f;
    private static readonly Dictionary<int, float> Expirations = new();
    private static float _nextBroadcastTime;

    internal static void Activate(int playerId)
    {
        float duration = GameModeManager.EffectiveInvincibleAfterSpawnSeconds;
        if (duration <= 0f)
        {
            Expirations.Remove(playerId);
            return;
        }

        SetExpiry(playerId, duration);
        Broadcast(playerId, duration);
    }

    internal static void ApplyRemote(int playerId, float durationSeconds)
    {
        if (playerId < 0 || durationSeconds <= 0f)
        {
            return;
        }

        SetExpiry(playerId, Mathf.Clamp(durationSeconds, 0f, 10f));
    }

    internal static bool IsProtected(PlayerHealth health)
    {
        if (health == null || !health)
        {
            return false;
        }

        if (health.playerValues?.playerClient != null)
        {
            return IsProtected(health.playerValues.playerClient.PlayerId);
        }

        foreach (KeyValuePair<int, ClientInstance> entry in ClientInstance.playerInstances)
        {
            if (entry.Value == null || !entry.Value || entry.Value.PlayerSpawner == null
                || entry.Value.PlayerSpawner.player != health)
            {
                continue;
            }

            return IsProtected(entry.Key);
        }

        return false;
    }

    internal static bool IsProtected(int playerId)
    {
        if (!Expirations.TryGetValue(playerId, out float expiry))
        {
            return false;
        }

        if (Time.unscaledTime < expiry)
        {
            return true;
        }

        Expirations.Remove(playerId);
        return false;
    }

    internal static IEnumerable<int> ActivePlayerIds()
    {
        List<int> expired = new();
        foreach (KeyValuePair<int, float> entry in Expirations)
        {
            if (Time.unscaledTime < entry.Value)
            {
                yield return entry.Key;
            }
            else
            {
                expired.Add(entry.Key);
            }
        }

        foreach (int playerId in expired)
        {
            Expirations.Remove(playerId);
        }
    }

    internal static void PeriodicPushIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost || Time.unscaledTime < _nextBroadcastTime)
        {
            return;
        }

        _nextBroadcastTime = Time.unscaledTime + BroadcastInterval;
        foreach (int playerId in ActivePlayerIds())
        {
            float remaining = GetRemainingSeconds(playerId);
            if (remaining > 0f)
            {
                Broadcast(playerId, remaining);
            }
        }
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        foreach (int playerId in ActivePlayerIds())
        {
            float remaining = GetRemainingSeconds(playerId);
            if (remaining > 0f)
            {
                MyceliumNetwork.RPCTarget(GameModeManager.ModId, nameof(Plugin.SyncSpawnProtection), player,
                    ReliableType.Reliable, playerId, remaining);
            }
        }
    }

    internal static void ResetForMatch()
    {
        Expirations.Clear();
    }

    internal static void ResetForLobbyLeft()
    {
        Expirations.Clear();
        _nextBroadcastTime = 0f;
    }

    private static void SetExpiry(int playerId, float durationSeconds)
    {
        if (playerId >= 0 && durationSeconds > 0f)
        {
            Expirations[playerId] = Time.unscaledTime + durationSeconds;
        }
    }

    private static float GetRemainingSeconds(int playerId)
    {
        return Expirations.TryGetValue(playerId, out float expiry)
            ? Mathf.Max(0f, expiry - Time.unscaledTime)
            : 0f;
    }

    private static void Broadcast(int playerId, float durationSeconds)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(GameModeManager.ModId, nameof(Plugin.SyncSpawnProtection),
                ReliableType.Reliable, playerId, durationSeconds);
        }
    }
}
