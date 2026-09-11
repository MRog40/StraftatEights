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
    internal const string SettingsLobbyDataKey = "StraftatEights_Juggernaut_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_Juggernaut_Live";
    internal const string WeaponName = "Minigun";
    internal const float BaseHealth = 200f / 25f;
    internal const float HealthPerKill = 50f / 25f;
    internal const float MovementMultiplier = 0.5f;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static bool Enabled;

    internal static int CurrentJuggernautPlayerId = -1;
    internal static int CurrentJuggernautKills;
    internal static int WinnerId = -1;
    internal static readonly Dictionary<int, int> Points = new();

    private static float _nextLoadoutCheckTime;
    private static readonly ModeSyncState Sync = new(livePushInterval: 1f);
    private static readonly Dictionary<int, float> PendingLoadouts = new();

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        CurrentJuggernautPlayerId = -1;
        CurrentJuggernautKills = 0;
        WinnerId = -1;
        Points.Clear();
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
        int revision = Sync.NextSettingsRevision();
        PublishSettingsSnapshot(revision);
        MyceliumNetwork.RPC(Plugin.JuggernautModId, nameof(Plugin.SyncJuggernautSettings), ReliableType.Reliable,
            SettingsRpcArgs(revision));
    }

    // Same reasoning as GlobalModifiersState.PeriodicPushIfHost: a single one-shot settings broadcast
    // can be silently dropped by a flaky Mycelium P2P session, so keep resending periodically while
    // hosting (the live-state broadcast in ServerTick already does this every second; settings didn't).
    internal static void PeriodicPushSettingsIfHost()
    {
        if (Sync.IsSettingsPushDue())
        {
            PushSettingsIfHost();
        }
    }

    internal static void PeriodicPushIfHost()
    {
        PeriodicPushSettingsIfHost();

        if (Enabled && GameModeManager.IsActive(GameMode.Juggernaut)
            && Sync.IsLivePushDue())
        {
            BroadcastLiveState();
        }
    }

    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        Plugin.Logger.LogInfo($"[Juggernaut] Lobby session started, IsHost={MyceliumNetwork.IsHost}");
        if (MyceliumNetwork.IsHost)
        {
            ApplySettingsFromHostConfig();
            ResetMatchState();
        }
        else
        {
            ApplyLobbySettingsSnapshot();
            ApplyLobbyLiveSnapshot();
        }
    }

    internal static void OnLobbyDataUpdated(List<string> keys)
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby)
        {
            return;
        }

        if (ModeLobbyDataSync.ContainsKey(keys, SettingsLobbyDataKey))
        {
            ApplyLobbySettingsSnapshot();
        }
        if (ModeLobbyDataSync.ContainsKey(keys, LiveLobbyDataKey))
        {
            ApplyLobbyLiveSnapshot();
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
        MyceliumNetwork.RPCTarget(Plugin.JuggernautModId, nameof(Plugin.SyncJuggernautSettings), player,
            ReliableType.Reliable, SettingsRpcArgs(Sync.SettingsRevision));
        MyceliumNetwork.RPCTarget(Plugin.JuggernautModId, nameof(Plugin.SyncJuggernautLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, CurrentJuggernautPlayerId, CurrentJuggernautKills,
            SerializePoints(),
            GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    private static object[] SettingsRpcArgs(int revision)
    {
        return new object[]
        {
            MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId,
            revision,
            Plugin.JuggernautEnabled.Value
        };
    }

    internal static void ApplyLiveState(CSteamID hostId, int juggernautPlayerId, int juggernautKills,
        string pointsData, int roundId, int revision, string source = "rpc")
    {
        if (juggernautPlayerId < -1 || juggernautKills < 0)
        {
            return;
        }
        if (!Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }
        CurrentJuggernautPlayerId = juggernautPlayerId;
        CurrentJuggernautKills = juggernautKills;
        Points.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(pointsData, PointsToWin))
        {
            Points[entry.Key] = entry.Value;
        }
    }

    // MyceliumNetworking's serializer supports string but not int[]/Dictionary, so points are packed
    // as "id:points;id:points"
    internal static string SerializePoints()
    {
        return ScoreCodec.Serialize(Points);
    }

    // Host-only: called every frame from GameManager.Update via JuggernautPatches
    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled)
        {
            return;
        }

        if (Sync.IsLivePushDue())
        {
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
                AwardPoints(killerId, ScoreRules.PointsPerKill);
                BecomeJuggernaut(killerId, PlayerLookup.GetPlayerNameTag(killerId) + " drew <color=red>first blood</color> and is the <color=#FF6A00><b>JUGGERNAUT</b></color>!");
            }
            return;
        }

        if (deadPlayerId == CurrentJuggernautPlayerId)
        {
            if (legitKill)
            {
                AwardPoints(killerId, ScoreRules.PointsPerJuggernautCrown);
                BecomeJuggernaut(killerId, PlayerLookup.GetPlayerNameTag(killerId) + " slayed the Juggernaut and <color=#FF6A00><b>claimed the crown</b></color>!");
            }
            return;
        }

        if (legitKill && killerId == CurrentJuggernautPlayerId)
        {
            CurrentJuggernautKills++;
            AwardPoints(killerId, ScoreRules.PointsPerKill);
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
        GameModeHud.ShowScorePopupForPlayer(playerId, amount);
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
        if (health == null && weapon.playerController != null)
        {
            health = weapon.playerController.GetComponent<PlayerHealth>();
        }

        if (health != null)
        {
            return IsCurrentJuggernaut(health);
        }

        return weapon.IsOwner && ClientInstance.Instance != null
            && ClientInstance.Instance.PlayerId == CurrentJuggernautPlayerId;
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
            WeaponService.GiveWeapon(playerId, WeaponName, unlimitedAmmo: true);
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
            FishNetCompatibility.TryRemoveHealth(health, -healthToAdd);
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
        FishNetCompatibility.TryRemoveHealth(health, -HealthPerKill);
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
        int revision = Sync.NextLiveRevision();
        ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, CurrentJuggernautPlayerId.ToString(),
            CurrentJuggernautKills.ToString(), SerializePoints());
        MyceliumNetwork.RPC(Plugin.JuggernautModId, nameof(Plugin.SyncJuggernautLiveState), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, CurrentJuggernautPlayerId, CurrentJuggernautKills, SerializePoints(),
            GameModeManager.RoundId, revision);
    }

    private static void PublishSettingsSnapshot(int revision)
    {
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.JuggernautEnabled.Value ? "1" : "0");
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("juggernaut", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 3, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[0], out int juggernautPlayerId)
            || !int.TryParse(fields[1], out int juggernautKills))
        {
            return;
        }

        ApplyLiveState(hostId, juggernautPlayerId, juggernautKills, fields[2], roundId, revision,
            ModeLobbyDataSync.Source("juggernaut", "live"));
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
