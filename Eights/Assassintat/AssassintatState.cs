using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class AssassintatState
{
    internal const string SettingsLobbyDataKey = "Eights_Assassintat_Settings";
    internal const string LiveLobbyDataKey = "Eights_Assassintat_Live";
    internal const string AssassintatWeaponName = "Silenzzio";
    internal const string KingWeaponName = "Taser";
    internal const string BodyguardWeaponName = "Glock";
    internal const int MinWeaponDelaySeconds = 5;
    internal const int MaxWeaponDelaySeconds = 30;
    private const int DefaultWeaponDelaySeconds = 15;
    internal const int PointsForAssassintatWin = AssassintatRules.PointsForAssassintatWin;
    internal const int PointsForKingSurvival = AssassintatRules.PointsForKingSurvival;
    internal const int PointsForBodyguardSurvival = AssassintatRules.PointsForBodyguardSurvival;
    internal const int PointsForBodyguardKill = AssassintatRules.PointsForBodyguardKill;
    internal const float DefaultTakeTimeLimitSeconds = ModeTimeoutRules.DefaultRoundSeconds;

    internal static bool Enabled;
    internal static int KingPlayerId { get; private set; } = -1;
    internal static int WinnerId { get; private set; } = -1;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static bool WeaponsUnlocked { get; private set; }
    internal static int WeaponDelaySeconds { get; private set; } = DefaultWeaponDelaySeconds;
    internal static float TakeTimeRemaining => Mathf.Max(0f, _takeTimeRemaining);
    internal static float RoleAnnouncementDuration => WeaponDelaySeconds + 10f;
    internal static bool LocalIsAssassintat { get; private set; }
    internal static bool LocalIsKing { get; private set; }
    internal static readonly Dictionary<int, int> Scores = new();

    private static int AssassintatPlayerId { get; set; } = -1;
    private static readonly HashSet<int> AlivePlayers = new();
    private static readonly HashSet<int> BodyguardPlayerIds = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static readonly ModeSyncState Sync = new();
    private static float _nextLoadoutCheckTime;
    private static float _nextClientLivePollTime;
    private static float _takeTimeRemaining;
    private static int _takeId;
    private static int _localRoleTakeId = -1;
    private static int _localRoleAnnouncedTakeId = -1;
    private static bool _localRoleAnnouncementPending;
    private static bool _startRetryPending;
    private static bool _takeEnding;

    internal static void ApplySettings(bool enabled)
    {
        if (GameModeManager.ShouldDeferModeDisable(GameMode.Assassintat, enabled))
        {
            return;
        }

        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.AssassintatEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.AssassintatEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.AssassintatModId, nameof(Plugin.SyncAssassintatSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision, Plugin.AssassintatEnabled.Value);
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

    internal static void PollLiveStateIfClient()
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !GameModeManager.IsActive(GameMode.Assassintat)
            || Time.unscaledTime < _nextClientLivePollTime)
        {
            return;
        }

        _nextClientLivePollTime = Time.unscaledTime + 1f;
        ApplyLobbyLiveSnapshot();
    }

    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        _nextClientLivePollTime = 0f;
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

    internal static void OnLobbyLeft()
    {
        Sync.ResetForLobby();
        _nextClientLivePollTime = 0f;
        ResetMatchState();
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

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.AssassintatModId, nameof(Plugin.SyncAssassintatSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId,
            Sync.SettingsRevision, Plugin.AssassintatEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.AssassintatModId, nameof(Plugin.SyncAssassintatLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, KingPlayerId, SerializeScores(),
            WinnerId, _takeId, WeaponsUnlocked, WeaponDelaySeconds,
            _takeTimeRemaining, GameModeManager.RoundId, Sync.LiveRevision);

        if (!TakeIsActive())
        {
            return;
        }

        int playerId = FindPlayerId(player);
        if (playerId < 0)
        {
            return;
        }

        AlivePlayers.Add(playerId);
        BodyguardPlayerIds.Add(playerId);
        Scores.TryAdd(playerId, 0);
        SendRoleToPlayer(player, playerId, true);
    }

    internal static void OnPlayerLeft(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        int playerId = PlayerLookup.FindPlayerId(player);
        bool wasAlive = playerId >= 0 && AlivePlayers.Contains(playerId);
        if (wasAlive && GameModeManager.IsActive(GameMode.Assassintat)
            && WinnerId < 0 && !_takeEnding)
        {
            OnServerKill(playerId, -1);
        }

        bool changed = wasAlive || (playerId >= 0 && Scores.Remove(playerId));
        PendingLoadouts.Remove(playerId);
        AlivePlayers.Remove(playerId);
        BodyguardPlayerIds.Remove(playerId);
        if (playerId >= 0 && AssassintatPlayerId == playerId)
        {
            AssassintatPlayerId = -1;
            changed = true;
        }
        if (playerId >= 0 && KingPlayerId == playerId)
        {
            KingPlayerId = -1;
            changed = true;
        }

        if (changed && GameModeManager.IsActive(GameMode.Assassintat))
        {
            BroadcastLiveState();
        }
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        _nextLoadoutCheckTime = 0f;
        _takeTimeRemaining = 0f;
        _takeId = 0;
        WeaponDelaySeconds = DefaultWeaponDelaySeconds;
        _localRoleTakeId = -1;
        _localRoleAnnouncedTakeId = -1;
        _localRoleAnnouncementPending = false;
        _startRetryPending = false;
        _takeEnding = false;
        AssassintatPlayerId = -1;
        KingPlayerId = -1;
        WinnerId = -1;
        WeaponsUnlocked = false;
        LocalIsAssassintat = false;
        LocalIsKing = false;
        AlivePlayers.Clear();
        BodyguardPlayerIds.Clear();
        PendingLoadouts.Clear();
        Scores.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, int kingPlayerId, string scoresData,
        int winnerId, int takeId, bool weaponsUnlocked, int weaponDelaySeconds,
        float takeTimeRemaining, int roundId, int revision, string source = "rpc")
    {
        int previousRoundId = Sync.LastLiveRoundId;
        if (kingPlayerId < -1 || winnerId < -1
            || weaponDelaySeconds < MinWeaponDelaySeconds
            || weaponDelaySeconds > MaxWeaponDelaySeconds
            || takeTimeRemaining < 0f
            || float.IsNaN(takeTimeRemaining) || float.IsInfinity(takeTimeRemaining)
            || takeTimeRemaining > DefaultTakeTimeLimitSeconds
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        KingPlayerId = kingPlayerId;
        _takeId = roundId != previousRoundId
            ? takeId
            : Math.Max(_takeId, takeId);
        WinnerId = winnerId;
        WeaponsUnlocked = weaponsUnlocked;
        WeaponDelaySeconds = weaponDelaySeconds;
        _takeTimeRemaining = takeTimeRemaining;
        Scores.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(scoresData, PointsToWin))
        {
            Scores[entry.Key] = entry.Value;
        }
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Assassintat) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        Scores.Clear();
        WinnerId = -1;
        StartTake();
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled || !MyceliumNetwork.IsHost
            || !GameModeManager.IsActive(GameMode.Assassintat)
            || !GameModeManager.IsRoundGameplayActive
            || WinnerId >= 0 || !TakeIsActive())
        {
            return;
        }

        _takeTimeRemaining = Mathf.Max(0f, _takeTimeRemaining - Mathf.Max(0f, deltaTime));
        if (_takeTimeRemaining <= 0f)
        {
            CompleteTimeoutWin();
        }
    }

    internal static void ClientTick(float deltaTime)
    {
        TryAnnounceLocalRole();
        if (MyceliumNetwork.IsHost || !Enabled
            || !GameModeManager.IsActive(GameMode.Assassintat)
            || !GameModeManager.IsRoundGameplayActive
            || WinnerId >= 0 || !TakeIsActive())
        {
            return;
        }

        _takeTimeRemaining = Mathf.Max(0f, _takeTimeRemaining - Mathf.Max(0f, deltaTime));
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Assassintat)
            || WinnerId >= 0 || _takeEnding)
        {
            return;
        }

        bool wasAlive = AlivePlayers.Remove(deadPlayerId);
        bool deadWasKing = deadPlayerId == KingPlayerId;
        bool deadWasAssassintat = deadPlayerId == AssassintatPlayerId;
        if (!deadWasKing && !deadWasAssassintat && wasAlive
            && !BodyguardPlayerIds.Contains(deadPlayerId))
        {
            Plugin.Logger.LogWarning($"[Assassintat] Repairing stale assassin role for dead player {deadPlayerId}; "
                + $"stored assassin={AssassintatPlayerId}, king={KingPlayerId}, take={_takeId}.");
            AssassintatPlayerId = deadPlayerId;
            deadWasAssassintat = true;
        }
        if (!wasAlive && !deadWasKing && !deadWasAssassintat)
        {
            return;
        }

        if (AssassintatRules.IsTerminalDeath(deadWasKing, deadWasAssassintat)
            && deadWasKing)
        {
            AwardScore(AssassintatPlayerId, AssassintatRules.GetAssassintatAward(deadWasKing));
            GameModeHud.BroadcastTakeResult("<b>"
                + PlayerLookup.GetPlayerNameTag(AssassintatPlayerId)
                + " won the take</b>\n<i>The king was eliminated</i>");
            AnnounceResult("The King was killed. The Assassintat was "
                + PlayerLookup.GetPlayerNameTag(AssassintatPlayerId) + ".");
            FinishTake();
            return;
        }

        if (deadWasAssassintat)
        {
            List<int> winningPlayerIds = new();
            if (KingPlayerId >= 0)
            {
                winningPlayerIds.Add(KingPlayerId);
            }
            foreach (int playerId in AlivePlayers)
            {
                if (playerId != KingPlayerId && playerId != AssassintatPlayerId
                    && BodyguardPlayerIds.Contains(playerId))
                {
                    winningPlayerIds.Add(playerId);
                }
            }

            foreach (int playerId in winningPlayerIds)
            {
                AwardScore(playerId, playerId == KingPlayerId
                    ? AssassintatRules.GetKingAward(deadWasAssassintat)
                    : AssassintatRules.GetBodyguardAward(deadWasAssassintat));
            }

            bool killerIsBodyguard = killerId >= 0 && killerId != deadPlayerId
                && killerId != KingPlayerId && killerId != AssassintatPlayerId;
            if (killerIsBodyguard)
            {
                AwardScore(killerId, AssassintatRules.GetBodyguardKillerAward(
                    deadWasAssassintat, killerIsBodyguard, killerId == deadPlayerId));
            }

            string kingLabel = KingPlayerId >= 0
                ? PlayerLookup.GetPlayerNameTag(KingPlayerId)
                : "the king";
            GameModeHud.BroadcastTakeResult("<b>" + kingLabel
                + " and the bodyguards won the take</b>\n<i>The assassin was eliminated</i>");
            AnnounceResult("The Assassintat was "
                + PlayerLookup.GetPlayerNameTag(AssassintatPlayerId) + " and was stopped.");
            _takeEnding = true;
            BroadcastLiveState();
            List<int> winningTeamIds = winningPlayerIds
                .Select(TeamAssignment.ResolveTeamId)
                .Where(teamId => teamId >= 0)
                .Distinct()
                .ToList();
            if (winningTeamIds.Count > 0)
            {
                GameModeManager.CompleteCustomRound(winningTeamIds);
            }
            else
            {
                Plugin.Logger.LogWarning("[Assassintat] Assassintat death had no valid winning players; "
                    + "ending the round without a team point.");
                GameModeManager.CompleteCustomRound(GameModeManager.NoWinningTeamId, false);
            }
            return;
        }

        BroadcastLiveState();
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Assassintat) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        if (!WeaponsUnlocked && !IsKingPlayer(playerId))
        {
            PendingLoadouts.Remove(playerId);
            ClearPlayerWeapons(playerId);
            return;
        }

        GiveWeapon(playerId);
    }

    internal static bool IsAllowedWeapon(Weapon weapon, int playerId)
    {
        if (weapon == null || (!WeaponsUnlocked && !IsKingPlayer(playerId)))
        {
            return false;
        }

        string? expected = GetExpectedWeaponName(playerId);
        return expected != null && weapon.name.StartsWith(expected, StringComparison.Ordinal);
    }

    internal static bool IsUnlimitedWeapon(Weapon weapon)
    {
        if (!WeaponsUnlocked && !IsKingPlayer(GetWeaponPlayerId(weapon)))
        {
            return false;
        }

        return IsAllowedWeapon(weapon, GetWeaponPlayerId(weapon));
    }

    internal static bool IsKingPlayer(int playerId) => playerId >= 0 && playerId == KingPlayerId;

    internal static void ApplyLocalRole(CSteamID hostId, int takeId, bool isAssassintat,
        bool isKing, bool announce, int weaponDelaySeconds)
    {
        if (hostId != MyceliumNetwork.LobbyHost || takeId < _localRoleTakeId
            || weaponDelaySeconds < MinWeaponDelaySeconds
            || weaponDelaySeconds > MaxWeaponDelaySeconds)
        {
            return;
        }

        _localRoleTakeId = takeId;
        WeaponDelaySeconds = weaponDelaySeconds;
        LocalIsAssassintat = isAssassintat;
        LocalIsKing = isKing;
        if (announce && _localRoleAnnouncedTakeId != takeId)
        {
            _localRoleAnnouncementPending = true;
            TryAnnounceLocalRole();
        }
    }

    private static void TryAnnounceLocalRole()
    {
        if (!_localRoleAnnouncementPending || _localRoleTakeId < 0
            || !Enabled || !GameModeManager.IsActive(GameMode.Assassintat)
            || !GameModeManager.IsRoundGameplayActive || GameModeManager.IsMatchOver
            || _localRoleAnnouncedTakeId == _localRoleTakeId)
        {
            return;
        }

        _localRoleAnnouncementPending = false;
        _localRoleAnnouncedTakeId = _localRoleTakeId;
        string roleText = LocalIsAssassintat
            ? "You are the <color=#CC2222><b>assassin</b></color>."
            : LocalIsKing
                ? "You are the <color=#35D05F><b>king</b></color>."
                : "You are a <color=#4D9BFF><b>bodyguard</b></color>.";
        GameModeHud.AnnounceTarget(roleText, RoleAnnouncementDuration);
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Assassintat) || !MyceliumNetwork.InLobby
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
            if (!WeaponsUnlocked && !IsKingPlayer(client.PlayerId))
            {
                if (pickup != null && ((pickup.objInHand != null && pickup.objInHand)
                    || (pickup.objInLeftHand != null && pickup.objInLeftHand)))
                {
                    WeaponService.ClearHeldWeapons(pickup);
                }
                continue;
            }

            Weapon? weapon = GetWeapon(pickup?.objInHand);
            if (weapon != null && IsAllowedWeapon(weapon, client.PlayerId))
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

    private static void StartTake()
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

        if (players.Count < 2)
        {
            AssassintatPlayerId = -1;
            KingPlayerId = -1;
            ScheduleStartTakeRetry();
            return;
        }

        _takeId++;
        _takeEnding = false;
        _takeTimeRemaining = DefaultTakeTimeLimitSeconds;
        WeaponDelaySeconds = UnityEngine.Random.Range(MinWeaponDelaySeconds,
            MaxWeaponDelaySeconds + 1);
        WeaponsUnlocked = false;
        _nextLoadoutCheckTime = 0f;
        PendingLoadouts.Clear();
        AlivePlayers.Clear();
        foreach (int playerId in players)
        {
            AlivePlayers.Add(playerId);
            Scores.TryAdd(playerId, 0);
        }

        AssassintatPlayerId = DistributionRandom.SelectPlayer("Assassintat", players);
        List<int> kingCandidates = players.Where(playerId => playerId != AssassintatPlayerId).ToList();
        KingPlayerId = DistributionRandom.SelectPlayer("King", kingCandidates);
        BodyguardPlayerIds.Clear();
        foreach (int playerId in players)
        {
            if (playerId != AssassintatPlayerId && playerId != KingPlayerId)
            {
                BodyguardPlayerIds.Add(playerId);
            }
        }

        ClearCurrentWeapons();
        SendRoleStates(true);
        EnsureLoadouts();
        BroadcastLiveState();
        Announce(PlayerLookup.GetPlayerNameTag(KingPlayerId)
            + " is the <color=#35D05F><b>KING</b></color>.");

        if (Plugin.Instance != null)
        {
            int token = _takeId;
            Plugin.Instance.StartCoroutine(UnlockWeaponsAfterDelay(token, SessionState.Generation,
                GameModeManager.RoundId, WeaponDelaySeconds));
        }
    }

    private static void ScheduleStartTakeRetry()
    {
        if (_startRetryPending || Plugin.Instance == null)
        {
            return;
        }

        _startRetryPending = true;
        Plugin.Instance.StartCoroutine(RetryStartTake(SessionState.Generation,
            GameModeManager.RoundId, _takeId));
    }

    private static IEnumerator RetryStartTake(int sessionGeneration, int roundId,
        int previousTakeId)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            yield return new WaitForSeconds(0.25f);
            if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
                || WinnerId >= 0 || _takeEnding || !GameModeManager.IsActive(GameMode.Assassintat))
            {
                break;
            }

            StartTake();
            if (_takeId > previousTakeId)
            {
                break;
            }
        }

        _startRetryPending = false;
    }

    private static IEnumerator UnlockWeaponsAfterDelay(int token, int sessionGeneration, int roundId,
        int weaponDelaySeconds)
    {
        yield return new WaitForSeconds(weaponDelaySeconds);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || token != _takeId || _takeEnding || WinnerId >= 0
            || !GameModeManager.IsActive(GameMode.Assassintat))
        {
            yield break;
        }

        WeaponsUnlocked = true;
        EnsureLoadouts();
        BroadcastLiveState();
    }

    private static void FinishTake()
    {
        BroadcastLiveState();
        if (WinnerId >= 0)
        {
            GameModeManager.CompleteCustomRound(TeamAssignment.ResolveTeamId(WinnerId));
            return;
        }

        BeginNextTake();
    }

    private static void CompleteTimeoutWin()
    {
        if (_takeEnding || WinnerId >= 0)
        {
            return;
        }

        if (KingPlayerId >= 0 && AlivePlayers.Contains(KingPlayerId))
        {
            AwardScore(KingPlayerId, AssassintatRules.GetKingAward(true));
        }

        foreach (int playerId in AlivePlayers)
        {
            if (playerId != KingPlayerId && playerId != AssassintatPlayerId)
            {
                AwardScore(playerId, AssassintatRules.GetBodyguardAward(true));
            }
        }

        GameModeHud.BroadcastTakeResult("<b>The King and bodyguards won the take</b>\n"
            + "<i>The Assassintat ran out of time</i>");
        Announce("The Assassintat did not eliminate the King in time.");
        FinishTake();
    }

    private static void BeginNextTake()
    {
        if (_takeEnding || Plugin.Instance == null)
        {
            return;
        }

        _takeEnding = true;
        WeaponsUnlocked = false;
        ClearCurrentWeapons();
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                GameModeRespawn.Schedule(client.PlayerId, GameModeManager.EffectiveRespawnDelaySeconds,
                    protectOnRespawn: false);
            }
        }

        Plugin.Instance.StartCoroutine(StartNextTakeAfterRespawn(
            GameModeManager.EffectiveRespawnDelaySeconds + 0.75f,
            SessionState.Generation, GameModeManager.RoundId));
    }

    private static IEnumerator StartNextTakeAfterRespawn(float delay, int sessionGeneration,
        int roundId)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.Assassintat))
        {
            yield break;
        }

        StartTake();
    }

    private static void GiveWeapon(int playerId)
    {
        string? weaponName = GetExpectedWeaponName(playerId);
        if (weaponName == null)
        {
            return;
        }

        PendingLoadouts[playerId] = Time.unscaledTime + 3f;
        WeaponService.GiveWeapon(playerId, weaponName, unlimitedAmmo: true);
    }

    private static string? GetExpectedWeaponName(int playerId)
    {
        if (playerId < 0)
        {
            return null;
        }

        if (MyceliumNetwork.IsHost)
        {
            if (playerId == AssassintatPlayerId)
            {
                return AssassintatWeaponName;
            }
            if (playerId == KingPlayerId)
            {
                return KingWeaponName;
            }
            return BodyguardWeaponName;
        }

        if (ClientInstance.Instance == null || ClientInstance.Instance.PlayerId != playerId)
        {
            return null;
        }

        return LocalIsAssassintat ? AssassintatWeaponName : LocalIsKing ? KingWeaponName : BodyguardWeaponName;
    }

    private static int GetWeaponPlayerId(Weapon weapon)
    {
        return weapon?.playerController?.GetComponent<PlayerHealth>()?.playerValues?.playerClient?.PlayerId
            ?? -1;
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

    private static void AwardScore(int playerId, int amount)
    {
        if (playerId < 0 || amount <= 0)
        {
            return;
        }

        Scores.TryGetValue(playerId, out int currentScore);
        int nextScore = ScoreRules.AddPoints(currentScore, amount, PointsToWin);
        int awardedPoints = nextScore - currentScore;
        Scores[playerId] = nextScore;
        if (awardedPoints > 0)
        {
            GameModeHud.ShowScorePopupForPlayer(playerId, awardedPoints);
        }
        if (WinnerId < 0 && nextScore >= PointsToWin)
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
            ApplyLocalRole(MyceliumNetwork.LobbyHost, _takeId,
                ClientInstance.Instance.PlayerId == AssassintatPlayerId,
                ClientInstance.Instance.PlayerId == KingPlayerId, announce, WeaponDelaySeconds);
        }
    }

    private static void SendRoleToPlayer(CSteamID target, int playerId, bool announce)
    {
        if (playerId < 0 || target.m_SteamID == 0)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.AssassintatModId, nameof(Plugin.SyncAssassintatRole), target,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, _takeId,
            playerId == AssassintatPlayerId, playerId == KingPlayerId, announce, WeaponDelaySeconds);
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

    private static bool TakeIsActive() => _takeId > 0 && !_takeEnding;

    private static Weapon? GetWeapon(GameObject? heldObject)
    {
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }

    private static string SerializeScores() => ScoreCodec.Serialize(Scores);

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            int revision = Sync.NextLiveRevision();
            ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
                GameModeManager.RoundId, revision, KingPlayerId.ToString(), SerializeScores(),
                WinnerId.ToString(), _takeId.ToString(), WeaponsUnlocked ? "1" : "0",
                WeaponDelaySeconds.ToString(),
                _takeTimeRemaining.ToString(CultureInfo.InvariantCulture));
            MyceliumNetwork.RPC(Plugin.AssassintatModId, nameof(Plugin.SyncAssassintatLiveState),
                ReliableType.Reliable, MyceliumNetwork.LobbyHost, KingPlayerId, SerializeScores(),
                WinnerId, _takeId, WeaponsUnlocked, WeaponDelaySeconds, _takeTimeRemaining,
                GameModeManager.RoundId, revision);
        }
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("assassintat", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 7, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[0], out int kingPlayerId)
            || !int.TryParse(fields[2], out int winnerId)
            || !int.TryParse(fields[3], out int takeId)
            || !LobbySnapshotCodec.TryParseBool(fields[4], out bool weaponsUnlocked)
            || !int.TryParse(fields[5], out int weaponDelaySeconds)
            || !float.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture,
                out float takeTimeRemaining))
        {
            return;
        }

        ApplyLiveState(hostId, kingPlayerId, fields[1], winnerId, takeId,
            weaponsUnlocked, weaponDelaySeconds, takeTimeRemaining, roundId, revision,
            ModeLobbyDataSync.Source("assassintat", "live"));
    }

    private static void Announce(string text)
    {
        GameModeHud.BroadcastAnnouncement(text);
    }

    private static void AnnounceResult(string text)
    {
        Announce(text);
    }
}
