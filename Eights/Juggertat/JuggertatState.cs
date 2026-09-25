using System.Collections.Generic;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

// Effective Juggertat game-mode state every peer enforces/displays locally; only the lobby host's
// config and kill/points bookkeeping is authoritative. See JuggertatConfig for the bound settings +
// RPC entry points, and JuggertatPatches for where this actually gets enforced/observed via Harmony.
internal static class JuggertatState
{
    internal const string SettingsLobbyDataKey = "Eights_Juggertat_Settings";
    internal const string LiveLobbyDataKey = "Eights_Juggertat_Live";
    internal const string WeaponName = "Minigun";
    internal const float BaseHealth = 200f / 25f;
    internal const float HealthPerKill = 50f / 25f;
    internal const float MovementMultiplier = 0.5f;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static bool Enabled;

    internal static int CurrentJuggertatPlayerId = -1;
    internal static int CurrentJuggertatKills;
    internal static int WinnerId = -1;
    internal static readonly Dictionary<int, int> Points = new();

    private static float _nextLoadoutCheckTime;
    private static readonly ModeSyncState Sync = new(livePushInterval: 1f);
    private static readonly Dictionary<int, float> PendingLoadouts = new();

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        CurrentJuggertatPlayerId = -1;
        CurrentJuggertatKills = 0;
        WinnerId = -1;
        Points.Clear();
        PendingLoadouts.Clear();
    }

    internal static void ApplySettings(bool enabled)
    {
        if (GameModeManager.ShouldDeferModeDisable(GameMode.Juggertat, enabled))
        {
            return;
        }

        bool wasEnabled = Enabled;
        Enabled = enabled;

        if (wasEnabled && !enabled)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig()
    {
        ApplySettings(Plugin.JuggertatEnabled.Value);
    }

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        PublishSettingsSnapshot(revision);
        MyceliumNetwork.RPC(Plugin.JuggertatModId, nameof(Plugin.SyncJuggertatSettings), ReliableType.Reliable,
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

        if (Enabled && GameModeManager.IsActive(GameMode.Juggertat)
            && Sync.IsLivePushDue())
        {
            BroadcastLiveState();
        }
    }

    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        if (MyceliumNetwork.IsHost)
        {
            ApplySettingsFromHostConfig();
            ResetMatchState();
            PushSettingsIfHost();
            BroadcastLiveState();
        }
        else
        {
            ApplyLobbySettingsSnapshot();
            ApplyLobbyLiveSnapshot();
        }
    }

    internal static void PollLiveStateIfClient()
    {
        ApplyLobbyLiveSnapshot();
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
        MyceliumNetwork.RPCTarget(Plugin.JuggertatModId, nameof(Plugin.SyncJuggertatSettings), player,
            ReliableType.Reliable, SettingsRpcArgs(Sync.SettingsRevision));
        MyceliumNetwork.RPCTarget(Plugin.JuggertatModId, nameof(Plugin.SyncJuggertatLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, CurrentJuggertatPlayerId, CurrentJuggertatKills,
            SerializePoints(),
            GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static void OnPlayerLeft(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        int playerId = PlayerLookup.FindPlayerId(player);
        bool changed = playerId >= 0 && Points.Remove(playerId);
        PendingLoadouts.Remove(playerId);
        if (playerId >= 0 && CurrentJuggertatPlayerId == playerId)
        {
            CurrentJuggertatPlayerId = -1;
            CurrentJuggertatKills = 0;
            changed = true;
        }

        if (changed && GameModeManager.IsActive(GameMode.Juggertat))
        {
            BroadcastLiveState();
        }
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
            Plugin.JuggertatEnabled.Value
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
        CurrentJuggertatPlayerId = juggernautPlayerId;
        CurrentJuggertatKills = juggernautKills;
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

        // Host-only: called every frame from Plugin.Update.
    internal static void ServerTick(float deltaTime)
    {
            if (!Enabled || !MyceliumNetwork.IsHost
                || !GameModeManager.IsActive(GameMode.Juggertat))
        {
            return;
        }

        if (Sync.IsLivePushDue())
        {
            BroadcastLiveState();
        }
    }

    // Host-only: called from the GameManager.PlayerDied kill hook via JuggertatPatches
    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || WinnerId >= 0)
        {
            return;
        }
        bool legitKill = killerId >= 0 && killerId != deadPlayerId;

        if (CurrentJuggertatPlayerId < 0)
        {
            if (legitKill)
            {
                AwardPoints(killerId, ScoreRules.PointsPerKill);
                BecomeJuggertat(killerId, PlayerLookup.GetPlayerNameTag(killerId) + " drew <color=red>first blood</color> and is the <color=#FF6A00><b>JUGGERNAUT</b></color>!");
            }
            return;
        }

        if (deadPlayerId == CurrentJuggertatPlayerId)
        {
            if (legitKill)
            {
                AwardPoints(killerId, ScoreRules.PointsPerJuggertatCrown);
                BecomeJuggertat(killerId, PlayerLookup.GetPlayerNameTag(killerId) + " slayed the Juggertat and <color=#FF6A00><b>claimed the crown</b></color>!");
            }
            return;
        }

        if (legitKill && killerId == CurrentJuggertatPlayerId)
        {
            CurrentJuggertatKills++;
            AwardPoints(killerId, ScoreRules.PointsPerKill);
            GrantHealthForKill(killerId);
            BroadcastLiveState();
        }
    }

    private static void BecomeJuggertat(int playerId, string announcement)
    {
        CurrentJuggertatPlayerId = playerId;
        CurrentJuggertatKills = 0;

        Announce(announcement);
        AnnounceTarget(playerId, "<color=#FF6A00><b>You are now the JUGGERNAUT!</b></color>");
        SetHealthToMax(playerId);
        GiveStartingWeapon(playerId);
        BroadcastLiveState();
    }

    private static void AwardPoints(int playerId, int amount)
    {
        Points.TryGetValue(playerId, out int value);
        int total = ScoreRules.AddPoints(value, amount, PointsToWin);
        int awardedPoints = total - value;
        Points[playerId] = total;
        if (awardedPoints > 0)
        {
            GameModeHud.ShowScorePopupForPlayer(playerId, awardedPoints);
        }
        if (total >= PointsToWin)
        {
            WinnerId = playerId;
            Announce(PlayerLookup.GetPlayerNameTag(playerId) + " reached " + PointsToWin + " points and won the round!");
            GameModeManager.CompleteCustomRound(TeamAssignment.ResolveTeamId(playerId));
        }
    }

    internal static bool IsCurrentJuggertat(PlayerHealth health)
    {
        return GameModeManager.IsActive(GameMode.Juggertat) && health.playerValues?.playerClient?.PlayerId == CurrentJuggertatPlayerId;
    }

    internal static bool IsCurrentJuggertat(FirstPersonController? controller)
    {
        if (controller == null)
        {
            return false;
        }
        PlayerHealth? health = controller.GetComponent<PlayerHealth>();
        return health != null && IsCurrentJuggertat(health);
    }

    internal static bool IsCurrentJuggertatWeapon(Weapon weapon)
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
            return IsCurrentJuggertat(health);
        }

        return weapon.IsOwner && ClientInstance.Instance != null
            && ClientInstance.Instance.PlayerId == CurrentJuggertatPlayerId;
    }

    internal static void ApplyHealth(PlayerHealth health)
    {
        if (!IsCurrentJuggertat(health))
        {
            return;
        }
        health.fullHealth = GetMaximumHealth();
    }

    internal static void EnsureLoadout()
    {
        if (!Enabled || CurrentJuggertatPlayerId < 0 || !MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost
            || WeaponService.IsFinalGameScreen || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + 1f;
        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(CurrentJuggertatPlayerId);
        PlayerManager? manager = health == null ? null : GameModeRespawn.FindManager(health);
        PlayerPickup? pickup = manager?.player?.playerPickupScript;
        GameObject? heldObject = pickup?.objInHand;
        Weapon? heldWeapon = heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
        if (heldWeapon != null && IsCurrentJuggertatWeapon(heldWeapon))
        {
            PendingLoadouts.Remove(CurrentJuggertatPlayerId);
            return;
        }

        if (!PendingLoadouts.TryGetValue(CurrentJuggertatPlayerId, out float retryTime) || Time.unscaledTime >= retryTime)
        {
            GiveStartingWeapon(CurrentJuggertatPlayerId);
        }
    }

    internal static void GiveStartingWeapon(int playerId)
    {
        if (Enabled && playerId == CurrentJuggertatPlayerId)
        {
            PendingLoadouts[playerId] = Time.unscaledTime + 5f;
            WeaponService.GiveWeapon(playerId, WeaponName, unlimitedAmmo: true);
        }
    }

    internal static float GetMaximumHealth()
    {
        return BaseHealth + CurrentJuggertatKills * HealthPerKill;
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
            MyceliumNetwork.RPCTarget(Plugin.JuggertatModId, nameof(Plugin.JuggertatAnnounceTarget),
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
            GameModeManager.RoundId, revision, CurrentJuggertatPlayerId.ToString(),
            CurrentJuggertatKills.ToString(), SerializePoints());
        MyceliumNetwork.RPC(Plugin.JuggertatModId, nameof(Plugin.SyncJuggertatLiveState), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, CurrentJuggertatPlayerId, CurrentJuggertatKills, SerializePoints(),
            GameModeManager.RoundId, revision);
    }

    private static void PublishSettingsSnapshot(int revision)
    {
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.JuggertatEnabled.Value ? "1" : "0");
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("juggertat", "settings")))
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
            ModeLobbyDataSync.Source("juggertat", "live"));
    }

    private static void Announce(string text)
    {
        GameModeHud.BroadcastAnnouncement(text);
    }
}
