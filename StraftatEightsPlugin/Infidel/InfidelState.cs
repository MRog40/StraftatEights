using System;
using System.Collections;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class InfidelState
{
    internal const string WeaponName = "AK-K";
    internal const int SpareMagazines = 2;
    internal const float InfidelHealth = 200f / 25f;
    internal const float TerroristHealth = 100f / 25f;
    internal const float MovementMultiplier = 0.7f;
    internal const float WeaponDelaySeconds = 10f;

    internal static bool Enabled;
    internal static int InfidelPlayerId { get; private set; } = -1;
    internal static int WinnerId { get; private set; } = -1;
    internal static int KillsToWin => GameModeManager.EffectivePointsToWin;
    internal static bool WeaponsUnlocked { get; private set; }
    internal static bool LocalIsInfidel { get; private set; }
    internal static readonly Dictionary<int, int> Scores = new();

    private static readonly HashSet<int> AlivePlayers = new();
    private static readonly HashSet<int> PendingHealthResets = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static float _nextLoadoutCheckTime;
    private static readonly ModeSyncState Sync = new();
    private static int _subRoundId;
    private static int _localRoleSubRoundId = -1;
    private static int _localRoleAnnouncedSubRoundId = -1;
    private static bool _startRetryPending;
    private static bool _subRoundEnding;

    internal static void ApplySettings(bool enabled)
    {
        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.InfidelEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        MyceliumNetwork.RPC(Plugin.InfidelModId, nameof(Plugin.SyncInfidelSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.NextSettingsRevision(),
            Plugin.InfidelEnabled.Value);
    }

    internal static void PeriodicPushSettingsIfHost()
    {
        if (Sync.IsSettingsPushDue())
        {
            PushSettingsIfHost();
        }
    }

    internal static void PeriodicPushIfHost()
    {
        if (!Sync.IsLivePushDue())
        {
            return;
        }

        BroadcastLiveState();
        SendRoleStates(true);
    }

    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        if (MyceliumNetwork.IsHost)
        {
            ApplySettingsFromHostConfig();
            ResetMatchState();
        }
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.InfidelModId, nameof(Plugin.SyncInfidelSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.InfidelEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.InfidelModId, nameof(Plugin.SyncInfidelLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SerializeScores(), WinnerId,
            _subRoundId, WeaponsUnlocked, GameModeManager.RoundId, Sync.LiveRevision);

        if (InfidelPlayerId >= 0 && SubRoundIsActive())
        {
            int playerId = player.m_SteamID == 0 ? -1 : FindPlayerId(player);
            if (playerId >= 0)
            {
                AlivePlayers.Add(playerId);
                PendingHealthResets.Add(playerId);
                Scores.TryAdd(playerId, 0);
            }
            SendRoleToPlayer(player, playerId, true);
        }
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static void ResetMatchState()
    {
        StopSubRoundTransition();
        Sync.ResetLiveState();
        _nextLoadoutCheckTime = 0f;
        _subRoundId = 0;
        _localRoleSubRoundId = -1;
        _localRoleAnnouncedSubRoundId = -1;
        _startRetryPending = false;
        _subRoundEnding = false;
        InfidelPlayerId = -1;
        WinnerId = -1;
        WeaponsUnlocked = false;
        LocalIsInfidel = false;
        AlivePlayers.Clear();
        PendingHealthResets.Clear();
        PendingLoadouts.Clear();
        Scores.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, string scoresData, int winnerId,
        int subRoundId, bool weaponsUnlocked, int roundId, int revision)
    {
        int previousRoundId = Sync.LastLiveRoundId;
        if (winnerId < -1 || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision))
        {
            return;
        }

        WinnerId = winnerId;
        _subRoundId = roundId != previousRoundId
            ? subRoundId
            : Math.Max(_subRoundId, subRoundId);
        WeaponsUnlocked = weaponsUnlocked;
        Scores.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(scoresData, KillsToWin))
        {
            Scores[entry.Key] = entry.Value;
        }
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infidel) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        Scores.Clear();
        WinnerId = -1;
        StartSubRound();
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infidel)
            || WinnerId >= 0 || _subRoundEnding)
        {
            return;
        }

        bool deadWasInfidel = deadPlayerId == InfidelPlayerId;
        AlivePlayers.Remove(deadPlayerId);

        if (killerId >= 0 && killerId != deadPlayerId)
        {
            bool killerIsInfidel = killerId == InfidelPlayerId;
            int killerAward = InfidelRules.GetKillerAward(deadWasInfidel, killerIsInfidel,
                deadWasInfidel ? AlivePlayers.Count : 0);
            if (killerAward > 0)
            {
                AwardScore(killerId, killerAward);
            }

            int infidelBonusAward = InfidelRules.GetInfidelBonusAward(deadWasInfidel, killerIsInfidel);
            if (infidelBonusAward > 0)
            {
                AwardScore(InfidelPlayerId, infidelBonusAward);
            }
        }

        BroadcastLiveState();
        if (WinnerId >= 0)
        {
            Announce(PlayerLookup.GetPlayerNameTag(WinnerId) + " reached " + KillsToWin
                + " points and won the round!");
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(WinnerId));
            return;
        }

        if (deadWasInfidel || (!deadWasInfidel && AlivePlayers.Count == 1))
        {
            BeginNextSubRound();
        }
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infidel) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        if (!WeaponsUnlocked)
        {
            PendingLoadouts.Remove(playerId);
            ClearPlayerWeapons(playerId);
            return;
        }

        GiveWeapon(playerId);
    }

    internal static void RequestHealthReset(int playerId)
    {
        if (playerId >= 0)
        {
            PendingHealthResets.Add(playerId);
        }
    }

    internal static void ApplyHealth(PlayerHealth health)
    {
        int playerId = health.playerValues?.playerClient?.PlayerId ?? -1;
        float maximumHealth = IsInfidel(health) ? InfidelHealth : TerroristHealth;
        health.fullHealth = maximumHealth;
        if (!health.IsServer)
        {
            return;
        }

        float healthDelta = maximumHealth - health.sync___get_value_health();
        bool resetHealth = playerId >= 0 && PendingHealthResets.Remove(playerId);
        if (!resetHealth && healthDelta >= 0f)
        {
            return;
        }

        HealthSettingsTuning.ApplyingPassiveHealth = true;
        try
        {
            FishNetCompatibility.TryRemoveHealth(health, -healthDelta);
        }
        finally
        {
            HealthSettingsTuning.ApplyingPassiveHealth = false;
        }
    }

    internal static bool IsInfidel(PlayerHealth health)
    {
        int playerId = health.playerValues?.playerClient?.PlayerId ?? -1;
        if (MyceliumNetwork.IsHost)
        {
            return playerId >= 0 && playerId == InfidelPlayerId;
        }

        return ClientInstance.Instance != null && ClientInstance.Instance.PlayerId == playerId
            && LocalIsInfidel;
    }

    internal static bool IsAllowedWeapon(Weapon weapon, int playerId)
    {
        return weapon != null && WeaponsUnlocked
            && weapon.name.StartsWith(WeaponName, StringComparison.Ordinal);
    }

    internal static void ApplyLocalRole(CSteamID hostId, int subRoundId, bool isInfidel, bool announce)
    {
        if (hostId != MyceliumNetwork.LobbyHost || subRoundId < _localRoleSubRoundId)
        {
            return;
        }

        _localRoleSubRoundId = subRoundId;
        LocalIsInfidel = isInfidel;
        if (announce && _localRoleAnnouncedSubRoundId != subRoundId)
        {
            _localRoleAnnouncedSubRoundId = subRoundId;
            GameModeHud.AnnounceTarget(isInfidel
                ? "You are the <color=#CC2222><b>INFIDEL</b></color>."
                : "You are a <color=#4D9BFF><b>TERRORIST</b></color>.", WeaponDelaySeconds);
        }
    }

    private static bool SubRoundIsActive()
    {
        return _subRoundId > 0 && !_subRoundEnding;
    }

    private static void StartSubRound()
    {
        if (WinnerId >= 0 || !MyceliumNetwork.IsHost)
        {
            return;
        }

        List<int> players = new();
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                players.Add(client.PlayerId);
            }
        }

        if (players.Count == 0)
        {
            InfidelPlayerId = -1;
            ScheduleStartSubRoundRetry();
            return;
        }

        _subRoundId++;
        _subRoundEnding = false;
        WeaponsUnlocked = false;
        _nextLoadoutCheckTime = 0f;
        PendingLoadouts.Clear();
        AlivePlayers.Clear();
        foreach (int playerId in players)
        {
            AlivePlayers.Add(playerId);
            PendingHealthResets.Add(playerId);
            Scores.TryAdd(playerId, 0);
        }

        InfidelPlayerId = players[UnityEngine.Random.Range(0, players.Count)];
        ClearCurrentWeapons();
        SendRoleStates(true);
        BroadcastLiveState();

        if (Plugin.Instance != null)
        {
            int token = _subRoundId;
            Plugin.Instance.StartCoroutine(UnlockWeaponsAfterDelay(token, SessionState.Generation,
                GameModeManager.RoundId));
        }
    }

    private static void ScheduleStartSubRoundRetry()
    {
        if (_startRetryPending || Plugin.Instance == null)
        {
            return;
        }

        _startRetryPending = true;
        Plugin.Instance.StartCoroutine(RetryStartSubRound(SessionState.Generation,
            GameModeManager.RoundId, _subRoundId));
    }

    private static IEnumerator RetryStartSubRound(int sessionGeneration, int roundId,
        int previousSubRoundId)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            yield return new WaitForSeconds(0.25f);
            if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
                || WinnerId >= 0 || _subRoundEnding || !GameModeManager.IsActive(GameMode.Infidel))
            {
                break;
            }

            StartSubRound();
            if (_subRoundId > previousSubRoundId)
            {
                break;
            }
        }

        _startRetryPending = false;
    }

    private static IEnumerator UnlockWeaponsAfterDelay(int token, int sessionGeneration, int roundId)
    {
        yield return new WaitForSeconds(WeaponDelaySeconds);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || token != _subRoundId || _subRoundEnding || WinnerId >= 0
            || !GameModeManager.IsActive(GameMode.Infidel))
        {
            yield break;
        }

        WeaponsUnlocked = true;
        EnsureLoadouts();
        BroadcastLiveState();
    }

    private static void BeginNextSubRound()
    {
        if (_subRoundEnding || Plugin.Instance == null)
        {
            return;
        }

        _subRoundEnding = true;
        WeaponsUnlocked = false;
        ClearCurrentWeapons();
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                GameModeRespawn.Schedule(client.PlayerId, GameModeManager.EffectiveRespawnDelaySeconds);
            }
        }

        Plugin.Instance.StartCoroutine(StartNextSubRoundAfterRespawn(
            GameModeManager.EffectiveRespawnDelaySeconds + 0.75f,
            SessionState.Generation, GameModeManager.RoundId));
    }

    private static IEnumerator StartNextSubRoundAfterRespawn(float delay, int sessionGeneration, int roundId)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.Infidel))
        {
            yield break;
        }

        StartSubRound();
    }

    private static void StopSubRoundTransition()
    {
        _subRoundEnding = false;
    }

    private static void ClearCurrentWeapons()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                ClearPlayerWeapons(client.PlayerId);
            }
        }
    }

    private static void ClearPlayerWeapons(int playerId)
    {
        if (!ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client)
            || client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner
            || client.PlayerSpawner.player == null || !client.PlayerSpawner.player)
        {
            return;
        }

        PlayerPickup? pickup = client.PlayerSpawner.player.playerPickupScript;
        if (pickup != null)
        {
            WeaponService.ClearHeldWeapons(pickup);
        }
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infidel) || !MyceliumNetwork.InLobby
            || !MyceliumNetwork.IsHost || WeaponService.IsFinalGameScreen
            || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + 0.5f;
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner
                || client.PlayerSpawner.player == null || !client.PlayerSpawner.player
                || !client.PlayerSpawner.player.gameObject.activeInHierarchy)
            {
                continue;
            }

            PlayerPickup? pickup = client.PlayerSpawner.player.playerPickupScript;
            if (!WeaponsUnlocked)
            {
                if (pickup != null && ((pickup.objInHand != null && pickup.objInHand)
                    || (pickup.objInLeftHand != null && pickup.objInLeftHand)))
                {
                    WeaponService.ClearHeldWeapons(pickup);
                }
                continue;
            }

            Weapon? weapon = GetWeapon(pickup?.objInHand);
            if (weapon != null && weapon.name.StartsWith(WeaponName, StringComparison.Ordinal))
            {
                PendingLoadouts.Remove(client.PlayerId);
                continue;
            }

            if (!PendingLoadouts.TryGetValue(client.PlayerId, out float retryTime)
                || Time.unscaledTime >= retryTime)
            {
                GiveWeapon(client.PlayerId);
            }
        }
    }

    private static void GiveWeapon(int playerId)
    {
        PendingLoadouts[playerId] = Time.unscaledTime + 3f;
        WeaponService.GiveWeapon(playerId, WeaponName, SpareMagazines);
    }

    private static void AwardScore(int playerId, int amount)
    {
        Scores.TryGetValue(playerId, out int currentScore);
        int nextScore = currentScore + amount;
        Scores[playerId] = nextScore;
        if (amount > 0)
        {
            GameModeHud.ShowScorePopupForPlayer(playerId, amount);
        }
        if (WinnerId < 0 && nextScore >= KillsToWin)
        {
            WinnerId = playerId;
        }
    }

    private static void SendRoleStates(bool announce)
    {
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client && client.PlayerSteamID != 0)
            {
                SendRoleToPlayer(new CSteamID(client.PlayerSteamID), client.PlayerId, announce);
            }
        }

        if (ClientInstance.Instance != null)
        {
            ApplyLocalRole(MyceliumNetwork.LobbyHost, _subRoundId,
                ClientInstance.Instance.PlayerId == InfidelPlayerId, announce);
        }
    }

    private static void SendRoleToPlayer(CSteamID target, int playerId, bool announce)
    {
        if (playerId < 0 || target.m_SteamID == 0)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.InfidelModId, nameof(Plugin.SyncInfidelRole), target,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, _subRoundId,
            playerId == InfidelPlayerId, announce);
    }

    private static int FindPlayerId(CSteamID steamId)
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

    private static Weapon? GetWeapon(GameObject? heldObject)
    {
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }

    private static string SerializeScores() => ScoreCodec.Serialize(Scores);

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.InfidelModId, nameof(Plugin.SyncInfidelLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeScores(), WinnerId, _subRoundId,
                WeaponsUnlocked, GameModeManager.RoundId, Sync.NextLiveRevision());
        }
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.InfidelModId, nameof(Plugin.InfidelAnnounce), ReliableType.Reliable, text);
        }
    }
}