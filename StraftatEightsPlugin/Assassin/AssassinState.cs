using System;
using System.Collections;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class AssassinState
{
    internal const string SettingsLobbyDataKey = "StraftatEights_Assassin_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_Assassin_Live";
    internal const string AssassinWeaponName = "Silenzzio";
    internal const string KingWeaponName = "Taser";
    internal const string BodyguardWeaponName = "Glock";
    internal const int MinWeaponDelaySeconds = 5;
    internal const int MaxWeaponDelaySeconds = 30;
    private const int DefaultWeaponDelaySeconds = 15;
    internal const int PointsForAssassinWin = AssassinRules.PointsForAssassinWin;
    internal const int PointsForKingSurvival = AssassinRules.PointsForKingSurvival;
    internal const int PointsForBodyguardSurvival = AssassinRules.PointsForBodyguardSurvival;
    internal const int PointsForBodyguardKill = AssassinRules.PointsForBodyguardKill;

    internal static bool Enabled;
    internal static int KingPlayerId { get; private set; } = -1;
    internal static int WinnerId { get; private set; } = -1;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static bool WeaponsUnlocked { get; private set; }
    internal static int WeaponDelaySeconds { get; private set; } = DefaultWeaponDelaySeconds;
    internal static float RoleAnnouncementDuration => WeaponDelaySeconds + 10f;
    internal static bool LocalIsAssassin { get; private set; }
    internal static bool LocalIsKing { get; private set; }
    internal static readonly Dictionary<int, int> Scores = new();

    private static int AssassinPlayerId { get; set; } = -1;
    private static readonly HashSet<int> AlivePlayers = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static readonly ModeSyncState Sync = new();
    private static float _nextLoadoutCheckTime;
    private static float _nextClientLivePollTime;
    private static int _takeId;
    private static int _localRoleTakeId = -1;
    private static int _localRoleAnnouncedTakeId = -1;
    private static bool _startRetryPending;
    private static bool _takeEnding;

    internal static void ApplySettings(bool enabled)
    {
        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.AssassinEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.AssassinEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.AssassinModId, nameof(Plugin.SyncAssassinSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision, Plugin.AssassinEnabled.Value);
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
        SendRoleStates(false);
    }

    internal static void PollLiveStateIfClient()
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !GameModeManager.IsActive(GameMode.Assassin)
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

        MyceliumNetwork.RPCTarget(Plugin.AssassinModId, nameof(Plugin.SyncAssassinSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId,
            Sync.SettingsRevision, Plugin.AssassinEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.AssassinModId, nameof(Plugin.SyncAssassinLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, KingPlayerId, SerializeScores(),
            WinnerId, _takeId, WeaponsUnlocked, WeaponDelaySeconds,
            GameModeManager.RoundId, Sync.LiveRevision);

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
        if (wasAlive && GameModeManager.IsActive(GameMode.Assassin)
            && WinnerId < 0 && !_takeEnding)
        {
            OnServerKill(playerId, -1);
        }

        bool changed = wasAlive || (playerId >= 0 && Scores.Remove(playerId));
        PendingLoadouts.Remove(playerId);
        AlivePlayers.Remove(playerId);
        if (playerId >= 0 && AssassinPlayerId == playerId)
        {
            AssassinPlayerId = -1;
            changed = true;
        }
        if (playerId >= 0 && KingPlayerId == playerId)
        {
            KingPlayerId = -1;
            changed = true;
        }

        if (changed && GameModeManager.IsActive(GameMode.Assassin))
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
        _takeId = 0;
        WeaponDelaySeconds = DefaultWeaponDelaySeconds;
        _localRoleTakeId = -1;
        _localRoleAnnouncedTakeId = -1;
        _startRetryPending = false;
        _takeEnding = false;
        AssassinPlayerId = -1;
        KingPlayerId = -1;
        WinnerId = -1;
        WeaponsUnlocked = false;
        LocalIsAssassin = false;
        LocalIsKing = false;
        AlivePlayers.Clear();
        PendingLoadouts.Clear();
        Scores.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, int kingPlayerId, string scoresData,
        int winnerId, int takeId, bool weaponsUnlocked, int weaponDelaySeconds,
        int roundId, int revision, string source = "rpc")
    {
        int previousRoundId = Sync.LastLiveRoundId;
        if (kingPlayerId < -1 || winnerId < -1
            || weaponDelaySeconds < MinWeaponDelaySeconds
            || weaponDelaySeconds > MaxWeaponDelaySeconds
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
        Scores.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(scoresData, PointsToWin))
        {
            Scores[entry.Key] = entry.Value;
        }
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Assassin) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        Scores.Clear();
        WinnerId = -1;
        StartTake();
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Assassin)
            || WinnerId >= 0 || _takeEnding || !AlivePlayers.Remove(deadPlayerId))
        {
            return;
        }

        bool deadWasKing = deadPlayerId == KingPlayerId;
        bool deadWasAssassin = deadPlayerId == AssassinPlayerId;
        if (AssassinRules.IsTerminalDeath(deadWasKing, deadWasAssassin)
            && deadWasKing)
        {
            AwardScore(AssassinPlayerId, AssassinRules.GetAssassinAward(deadWasKing));
            GameModeHud.BroadcastTakeResult("<b>"
                + PlayerLookup.GetPlayerNameTag(AssassinPlayerId)
                + " won the take</b>\n<i>The king was eliminated</i>");
            AnnounceResult("The King was killed. The Assassin was "
                + PlayerLookup.GetPlayerNameTag(AssassinPlayerId) + ".");
            FinishTake();
            return;
        }

        if (deadWasAssassin)
        {
            AwardScore(KingPlayerId, AssassinRules.GetKingAward(deadWasAssassin));
            foreach (int playerId in Scores.Keys)
            {
                if (playerId != KingPlayerId && playerId != AssassinPlayerId)
                {
                    AwardScore(playerId, AssassinRules.GetBodyguardAward(deadWasAssassin));
                }
            }

            bool killerIsBodyguard = killerId >= 0 && killerId != deadPlayerId
                && killerId != KingPlayerId && killerId != AssassinPlayerId;
            if (killerIsBodyguard)
            {
                AwardScore(killerId, AssassinRules.GetBodyguardKillerAward(
                    deadWasAssassin, killerIsBodyguard, killerId == deadPlayerId));
            }

            string kingLabel = KingPlayerId >= 0
                ? PlayerLookup.GetPlayerNameTag(KingPlayerId)
                : "the king";
            GameModeHud.BroadcastTakeResult("<b>" + kingLabel
                + " and the bodyguards won the take</b>\n<i>The assassin was eliminated</i>");
            AnnounceResult("The Assassin was "
                + PlayerLookup.GetPlayerNameTag(AssassinPlayerId) + " and was stopped.");
            FinishTake();
            return;
        }

        BroadcastLiveState();
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Assassin) || !MyceliumNetwork.IsHost)
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

    internal static void ApplyLocalRole(CSteamID hostId, int takeId, bool isAssassin,
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
        LocalIsAssassin = isAssassin;
        LocalIsKing = isKing;
        if (announce && GameModeManager.IsActive(GameMode.Assassin)
            && GameModeManager.Phase == GameModePhase.ActiveRound
            && !GameModeManager.IsMatchOver && _localRoleAnnouncedTakeId != takeId)
        {
            _localRoleAnnouncedTakeId = takeId;
            string roleText = isAssassin
                ? "You are the <color=#CC2222><b>assassin</b></color>."
                : isKing
                    ? "You are the <color=#35D05F><b>king</b></color>."
                    : "You are a <color=#4D9BFF><b>bodyguard</b></color>.";
            GameModeHud.AnnounceTarget(roleText, RoleAnnouncementDuration);
        }
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Assassin) || !MyceliumNetwork.InLobby
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
            AssassinPlayerId = -1;
            KingPlayerId = -1;
            ScheduleStartTakeRetry();
            return;
        }

        _takeId++;
        _takeEnding = false;
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

        int assassinIndex = UnityEngine.Random.Range(0, players.Count);
        AssassinPlayerId = players[assassinIndex];
        int kingIndex = UnityEngine.Random.Range(0, players.Count - 1);
        if (kingIndex >= assassinIndex)
        {
            kingIndex++;
        }
        KingPlayerId = players[kingIndex];

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
                || WinnerId >= 0 || _takeEnding || !GameModeManager.IsActive(GameMode.Assassin))
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
            || !GameModeManager.IsActive(GameMode.Assassin))
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
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(WinnerId));
            return;
        }

        BeginNextTake();
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
            || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.Assassin))
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
            if (playerId == AssassinPlayerId)
            {
                return AssassinWeaponName;
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

        return LocalIsAssassin ? AssassinWeaponName : LocalIsKing ? KingWeaponName : BodyguardWeaponName;
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
        int nextScore = currentScore + amount;
        Scores[playerId] = nextScore;
        GameModeHud.ShowScorePopupForPlayer(playerId, amount);
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
                ClientInstance.Instance.PlayerId == AssassinPlayerId,
                ClientInstance.Instance.PlayerId == KingPlayerId, announce, WeaponDelaySeconds);
        }
    }

    private static void SendRoleToPlayer(CSteamID target, int playerId, bool announce)
    {
        if (playerId < 0 || target.m_SteamID == 0)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.AssassinModId, nameof(Plugin.SyncAssassinRole), target,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, _takeId,
            playerId == AssassinPlayerId, playerId == KingPlayerId, announce, WeaponDelaySeconds);
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
                WeaponDelaySeconds.ToString());
            MyceliumNetwork.RPC(Plugin.AssassinModId, nameof(Plugin.SyncAssassinLiveState),
                ReliableType.Reliable, MyceliumNetwork.LobbyHost, KingPlayerId, SerializeScores(),
                WinnerId, _takeId, WeaponsUnlocked, WeaponDelaySeconds,
                GameModeManager.RoundId, revision);
        }
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("assassin", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 6, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[0], out int kingPlayerId)
            || !int.TryParse(fields[2], out int winnerId)
            || !int.TryParse(fields[3], out int takeId)
            || !LobbySnapshotCodec.TryParseBool(fields[4], out bool weaponsUnlocked)
            || !int.TryParse(fields[5], out int weaponDelaySeconds))
        {
            return;
        }

        ApplyLiveState(hostId, kingPlayerId, fields[1], winnerId, takeId,
            weaponsUnlocked, weaponDelaySeconds, roundId, revision,
            ModeLobbyDataSync.Source("assassin", "live"));
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.AssassinModId, nameof(Plugin.AssassinAnnounce),
                ReliableType.Reliable, text);
        }
    }

    private static void AnnounceResult(string text)
    {
        Announce(text);
    }
}
