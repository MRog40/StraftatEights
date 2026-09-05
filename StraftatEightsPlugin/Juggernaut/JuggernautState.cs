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
    internal const string WeaponName = "Minigun";
    internal const float BaseHealth = 200f;
    internal const float HealthPerKill = 50f;
    internal const float MovementMultiplier = 0.5f;
    internal const int PointsToWin = 10;
    internal static bool Enabled;

    internal static int CurrentJuggernautPlayerId = -1;
    internal static int CurrentJuggernautKills;
    internal static int WinnerId = -1;
    internal static readonly Dictionary<int, int> Points = new();

    private static float _nextBroadcastTime;
    private static float _nextLoadoutCheckTime;
    private static readonly Dictionary<int, float> PendingLoadouts = new();

    internal static void ResetMatchState()
    {
        CurrentJuggernautPlayerId = -1;
        CurrentJuggernautKills = 0;
        WinnerId = -1;
        Points.Clear();
        _nextBroadcastTime = 0f;
        PendingLoadouts.Clear();
    }

    internal static void ApplySettings(bool enabled)
    {
        bool wasEnabled = Enabled;
        Enabled = enabled;

        if (wasEnabled && !enabled)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig()
    {
        ApplySettings(Plugin.JuggernautEnabled.Value);
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

    private static float _nextPeriodicSettingsPushTime;

    // Same reasoning as GlobalModifiersState.PeriodicPushIfHost: a single one-shot settings broadcast
    // can be silently dropped by a flaky Mycelium P2P session, so keep resending periodically while
    // hosting (the live-state broadcast in ServerTick already does this every second; settings didn't).
    internal static void PeriodicPushSettingsIfHost()
    {
        if (!HostSettingsSync.IsDue(ref _nextPeriodicSettingsPushTime))
        {
            return;
        }
        PushSettingsIfHost();
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
        MyceliumNetwork.RPCTarget(Plugin.JuggernautModId, nameof(Plugin.SyncJuggernautLiveState), player,
            ReliableType.Reliable, CurrentJuggernautPlayerId, CurrentJuggernautKills, SerializePoints());
    }

    private static object[] SettingsRpcArgs()
    {
        return new object[]
        {
            Plugin.JuggernautEnabled.Value
        };
    }

    internal static void ApplyLiveState(int juggernautPlayerId, int juggernautKills, string pointsData)
    {
        CurrentJuggernautPlayerId = juggernautPlayerId;
        CurrentJuggernautKills = juggernautKills;
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

        if (Time.unscaledTime >= _nextBroadcastTime)
        {
            _nextBroadcastTime = Time.unscaledTime + 1f;
            BroadcastLiveState();
        }
    }

    // Host-only: called from the GameManager.PlayerDied kill hook via JuggernautPatches
    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || WinnerId >= 0)
        {
            return;
        }
        bool legitKill = killerId >= 0 && killerId != deadPlayerId;

        if (CurrentJuggernautPlayerId < 0)
        {
            if (legitKill)
            {
                AwardPoints(killerId, 1);
                BecomeJuggernaut(killerId, PlayerLookup.GetPlayerNameTag(killerId) + " drew <color=red>first blood</color> and is the <color=#FF6A00><b>JUGGERNAUT</b></color>!");
            }
            return;
        }

        if (deadPlayerId == CurrentJuggernautPlayerId)
        {
            if (legitKill)
            {
                AwardPoints(killerId, 2);
                BecomeJuggernaut(killerId, PlayerLookup.GetPlayerNameTag(killerId) + " slayed the Juggernaut and <color=#FF6A00><b>claimed the crown</b></color>!");
            }
            return;
        }

        if (legitKill && killerId == CurrentJuggernautPlayerId)
        {
            CurrentJuggernautKills++;
            AwardPoints(killerId, 1);
            GrantHealthForKill(killerId);
            BroadcastLiveState();
        }
    }

    private static void BecomeJuggernaut(int playerId, string announcement)
    {
        CurrentJuggernautPlayerId = playerId;
        CurrentJuggernautKills = 0;

        Announce(announcement);
        AnnounceTarget(playerId, "<color=#FF6A00><b>You are now the JUGGERNAUT!</b></color>");
        SetHealthToMax(playerId);
        GiveStartingWeapon(playerId);
        BroadcastLiveState();
    }

    private static void AwardPoints(int playerId, int amount)
    {
        Points.TryGetValue(playerId, out int value);
        int total = value + amount;
        Points[playerId] = total;
        if (total >= PointsToWin)
        {
            WinnerId = playerId;
            Announce(PlayerLookup.GetPlayerNameTag(playerId) + " reached " + PointsToWin + " points and won the round!");
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(playerId));
        }
    }

    internal static bool IsCurrentJuggernaut(PlayerHealth health)
    {
        return GameModeManager.IsActive(GameMode.Juggernaut) && health.playerValues?.playerClient?.PlayerId == CurrentJuggernautPlayerId;
    }

    internal static bool IsCurrentJuggernaut(FirstPersonController? controller)
    {
        if (controller == null)
        {
            return false;
        }
        PlayerHealth? health = controller.GetComponent<PlayerHealth>();
        return health != null && IsCurrentJuggernaut(health);
    }

    internal static bool IsCurrentJuggernautWeapon(Weapon weapon)
    {
        if (weapon == null || !weapon.name.StartsWith(WeaponName, System.StringComparison.Ordinal))
        {
            return false;
        }

        PlayerHealth? health = weapon.GetComponentInParent<PlayerHealth>();
        if (health == null && weapon.rootObject != null)
        {
            health = weapon.rootObject.GetComponent<PlayerHealth>();
        }
        return health != null && IsCurrentJuggernaut(health);
    }

    internal static void ApplyHealth(PlayerHealth health)
    {
        if (!IsCurrentJuggernaut(health))
        {
            return;
        }
        health.fullHealth = GetMaximumHealth();
    }

    internal static void EnsureLoadout()
    {
        if (!Enabled || CurrentJuggernautPlayerId < 0 || !MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost
            || WeaponService.IsFinalGameScreen || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + 1f;
        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(CurrentJuggernautPlayerId);
        PlayerManager? manager = health == null ? null : GameModeRespawn.FindManager(health);
        PlayerPickup? pickup = manager?.player?.playerPickupScript;
        GameObject? heldObject = pickup?.objInHand;
        Weapon? heldWeapon = heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
        if (heldWeapon != null && IsCurrentJuggernautWeapon(heldWeapon))
        {
            PendingLoadouts.Remove(CurrentJuggernautPlayerId);
            return;
        }

        if (!PendingLoadouts.TryGetValue(CurrentJuggernautPlayerId, out float retryTime) || Time.unscaledTime >= retryTime)
        {
            GiveStartingWeapon(CurrentJuggernautPlayerId);
        }
    }

    internal static void GiveStartingWeapon(int playerId)
    {
        if (Enabled && playerId == CurrentJuggernautPlayerId)
        {
            PendingLoadouts[playerId] = Time.unscaledTime + 5f;
            WeaponService.GiveWeapon(playerId, WeaponName);
        }
    }

    internal static float GetMaximumHealth()
    {
        return BaseHealth + CurrentJuggernautKills * HealthPerKill;
    }

    private static void SetHealthToMax(int playerId)
    {
        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(playerId);
        if (health == null || !health.IsServer)
        {
            return;
        }

        float maximumHealth = GetMaximumHealth();
        health.fullHealth = maximumHealth;
        float healthToAdd = maximumHealth - health.sync___get_value_health();
        if (!UnityEngine.Mathf.Approximately(healthToAdd, 0f))
        {
            health.RpcLogic___RemoveHealth_431000436(-healthToAdd);
        }
    }

    private static void GrantHealthForKill(int playerId)
    {
        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(playerId);
        if (health == null || !health.IsServer)
        {
            return;
        }

        health.fullHealth = GetMaximumHealth();
        health.RpcLogic___RemoveHealth_431000436(-HealthPerKill);
    }

    private static void AnnounceTarget(int playerId, string text)
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        if (ClientInstance.Instance != null && ClientInstance.Instance.PlayerId == playerId && PauseManager.Instance != null)
        {
            GameModeHud.AnnounceTarget(ClientInstance.ReplaceAllPlayerNameTags(text));
        }

        if (ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client) && client.PlayerSteamID != 0)
        {
            MyceliumNetwork.RPCTarget(Plugin.JuggernautModId, nameof(Plugin.JuggernautAnnounceTarget),
                new CSteamID(client.PlayerSteamID), ReliableType.Reliable, text);
        }
    }

    internal static void BroadcastLiveState()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        MyceliumNetwork.RPC(Plugin.JuggernautModId, nameof(Plugin.SyncJuggernautLiveState), ReliableType.Reliable,
            CurrentJuggernautPlayerId, CurrentJuggernautKills, SerializePoints());
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
