using System.Collections.Generic;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

// Effective Juggernaut game-mode state every peer enforces/displays locally; only the lobby host's
// config and kill/points bookkeeping is authoritative. See JuggernautConfig for the bound settings +
// RPC entry points, and JuggernautPatches for where this actually gets enforced/observed via Harmony.
internal static class JuggernautState
{
    internal static bool Enabled;
    internal static int PointsPerSecond = 5;
    internal static int BonusHealthOnCrown = 25;
    internal static int HealthPerKill = 10;
    internal static float SpeedMultiplier = 1.25f;
    internal static float RespawnDelaySeconds = 3f;
    internal static bool ShowOutline = true;
    internal static bool ShowScoreboard = true;

    internal static int CurrentJuggernautPlayerId = -1;
    internal static readonly Dictionary<int, int> Points = new();

    private static float _pointAccumulator;
    private static float _nextBroadcastTime;

    internal static void ResetMatchState()
    {
        CurrentJuggernautPlayerId = -1;
        Points.Clear();
        _pointAccumulator = 0f;
        _nextBroadcastTime = 0f;
    }

    internal static void ApplySettings(bool enabled, int pointsPerSecond, int bonusHealthOnCrown, int healthPerKill, int speedPercent, float respawnDelaySeconds, bool showOutline, bool showScoreboard)
    {
        bool wasEnabled = Enabled;
        Enabled = enabled;
        PointsPerSecond = pointsPerSecond;
        BonusHealthOnCrown = bonusHealthOnCrown;
        HealthPerKill = healthPerKill;
        SpeedMultiplier = speedPercent / 100f;
        RespawnDelaySeconds = respawnDelaySeconds;
        ShowOutline = showOutline;
        ShowScoreboard = showScoreboard;

        if (wasEnabled && !enabled)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig()
    {
        ApplySettings(Plugin.JuggernautEnabled.Value, Plugin.JuggernautPointsPerSecond.Value, Plugin.JuggernautBonusHealthOnCrown.Value,
            Plugin.JuggernautHealthPerKill.Value, Plugin.JuggernautSpeedPercent.Value, Plugin.JuggernautRespawnDelaySeconds.Value,
            Plugin.JuggernautShowOutline.Value, Plugin.JuggernautShowScoreboard.Value);
    }

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplySettingsFromHostConfig();
        Plugin.Logger.LogInfo($"[Juggernaut] Host broadcasting settings to {MyceliumNetwork.PlayerCount} player(s)");
        MyceliumNetwork.RPC(Plugin.JuggernautModId, nameof(Plugin.SyncJuggernautSettings), ReliableType.Reliable, SettingsRpcArgs());
    }

    internal static void OnLobbyEntered()
    {
        Plugin.Logger.LogInfo($"[Juggernaut] Lobby session started, IsHost={MyceliumNetwork.IsHost}");
        if (MyceliumNetwork.IsHost)
        {
            ApplySettingsFromHostConfig();
            ResetMatchState();
        }
    }

    // Late joiners won't have received earlier broadcasts, so catch them up directly
    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        Plugin.Logger.LogInfo($"[Juggernaut] Sending catch-up settings/state to newly joined player {player}");
        MyceliumNetwork.RPCTarget(Plugin.JuggernautModId, nameof(Plugin.SyncJuggernautSettings), player, ReliableType.Reliable, SettingsRpcArgs());
        MyceliumNetwork.RPCTarget(Plugin.JuggernautModId, nameof(Plugin.SyncJuggernautLiveState), player, ReliableType.Reliable, CurrentJuggernautPlayerId, SerializePoints());
    }

    private static object[] SettingsRpcArgs()
    {
        return new object[]
        {
            Plugin.JuggernautEnabled.Value, Plugin.JuggernautPointsPerSecond.Value, Plugin.JuggernautBonusHealthOnCrown.Value,
            Plugin.JuggernautHealthPerKill.Value, Plugin.JuggernautSpeedPercent.Value, Plugin.JuggernautRespawnDelaySeconds.Value,
            Plugin.JuggernautShowOutline.Value, Plugin.JuggernautShowScoreboard.Value
        };
    }

    internal static void ApplyLiveState(int juggernautPlayerId, string pointsData)
    {
        CurrentJuggernautPlayerId = juggernautPlayerId;
        Points.Clear();
        if (string.IsNullOrEmpty(pointsData))
        {
            return;
        }
        foreach (string entry in pointsData.Split(';'))
        {
            int sep = entry.IndexOf(':');
            if (sep > 0 && int.TryParse(entry.Substring(0, sep), out int id) && int.TryParse(entry.Substring(sep + 1), out int pts))
            {
                Points[id] = pts;
            }
        }
    }

    // MyceliumNetworking's serializer supports string but not int[]/Dictionary, so points are packed
    // as "id:points;id:points"
    internal static string SerializePoints()
    {
        StringBuilder sb = new();
        foreach (KeyValuePair<int, int> kv in Points)
        {
            if (sb.Length > 0)
            {
                sb.Append(';');
            }
            sb.Append(kv.Key).Append(':').Append(kv.Value);
        }
        return sb.ToString();
    }

    // Host-only: called every frame from GameManager.Update via JuggernautPatches
    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled)
        {
            return;
        }

        if (CurrentJuggernautPlayerId >= 0)
        {
            PlayerHealth? health = PlayerLookup.FindPlayerHealthById(CurrentJuggernautPlayerId);
            if (health != null && health.gameObject.activeInHierarchy && health.health > 0f)
            {
                _pointAccumulator += deltaTime;
                while (_pointAccumulator >= 1f)
                {
                    _pointAccumulator -= 1f;
                    AddPoints(CurrentJuggernautPlayerId, PointsPerSecond);
                }
            }
            else
            {
                _pointAccumulator = 0f;
            }
        }

        if (Time.unscaledTime >= _nextBroadcastTime)
        {
            _nextBroadcastTime = Time.unscaledTime + 1f;
            BroadcastLiveState();
        }
    }

    // Host-only: called from the GameManager.PlayerDied kill hook via JuggernautPatches
    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled)
        {
            return;
        }
        bool legitKill = killerId >= 0 && killerId != deadPlayerId;

        if (CurrentJuggernautPlayerId < 0)
        {
            if (legitKill)
            {
                BecomeJuggernaut(killerId, PlayerLookup.GetPlayerNameTag(killerId) + " drew <color=red>first blood</color> and is the <color=#FF6A00><b>JUGGERNAUT</b></color>!");
            }
            return;
        }

        if (deadPlayerId == CurrentJuggernautPlayerId)
        {
            if (legitKill)
            {
                BecomeJuggernaut(killerId, PlayerLookup.GetPlayerNameTag(killerId) + " slayed the Juggernaut and <color=#FF6A00><b>claimed the crown</b></color>!");
            }
            return;
        }

        if (legitKill && killerId == CurrentJuggernautPlayerId && HealthPerKill > 0)
        {
            PlayerHealth? killerHealth = PlayerLookup.FindPlayerHealthById(killerId);
            if (killerHealth != null && killerHealth.IsServer && killerHealth.gameObject.activeInHierarchy && killerHealth.health > 0f)
            {
                killerHealth.RpcLogic___RemoveHealth_431000436(-HealthPerKill);
            }
        }
    }

    private static void BecomeJuggernaut(int playerId, string announcement)
    {
        CurrentJuggernautPlayerId = playerId;
        _pointAccumulator = 0f;

        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(playerId);
        if (health != null && health.IsServer && health.gameObject.activeInHierarchy && health.health > 0f && BonusHealthOnCrown > 0)
        {
            health.RpcLogic___RemoveHealth_431000436(-BonusHealthOnCrown);
        }

        Announce(announcement);
        BroadcastLiveState();
    }

    private static void AddPoints(int playerId, int amount)
    {
        Points.TryGetValue(playerId, out int value);
        Points[playerId] = value + amount;
    }

    internal static void BroadcastLiveState()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        MyceliumNetwork.RPC(Plugin.JuggernautModId, nameof(Plugin.SyncJuggernautLiveState), ReliableType.Reliable, CurrentJuggernautPlayerId, SerializePoints());
    }

    private static void Announce(string text)
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        MyceliumNetwork.RPC(Plugin.JuggernautModId, nameof(Plugin.JuggernautAnnounce), ReliableType.Reliable, text);
    }
}
