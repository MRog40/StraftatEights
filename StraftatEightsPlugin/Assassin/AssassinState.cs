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
    internal const float WeaponDelaySeconds = 15f;
    internal const float RoleAnnouncementDuration = WeaponDelaySeconds + 10f;
    internal const int PointsForAssassinWin = AssassinRules.PointsForAssassinWin;
    internal const int PointsForKingSurvival = AssassinRules.PointsForKingSurvival;
    internal const int PointsForBodyguardSurvival = AssassinRules.PointsForBodyguardSurvival;
    internal const int PointsForBodyguardKill = AssassinRules.PointsForBodyguardKill;

    internal static bool Enabled;
    internal static int KingPlayerId { get; private set; } = -1;
    internal static int WinnerId { get; private set; } = -1;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static bool WeaponsUnlocked { get; private set; }
    internal static bool LocalIsAssassin { get; private set; }
    internal static bool LocalIsKing { get; private set; }
    internal static readonly Dictionary<int, int> Scores = new();

    private static int AssassinPlayerId { get; set; } = -1;
    private static readonly HashSet<int> AlivePlayers = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static readonly ModeSyncState Sync = new();
    private static float _nextLoadoutCheckTime;
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

    internal static void OnLobbyLeft()
    {
        Sync.ResetForLobby();
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
            WinnerId, _subRoundId, WeaponsUnlocked, GameModeManager.RoundId, Sync.LiveRevision);

        if (!SubRoundIsActive())
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

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        _nextLoadoutCheckTime = 0f;
        _subRoundId = 0;
        _localRoleSubRoundId = -1;
        _localRoleAnnouncedSubRoundId = -1;
        _startRetryPending = false;
        _subRoundEnding = false;
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
        int winnerId, int subRoundId, bool weaponsUnlocked, int roundId, int revision,
        string source = "rpc")
    {
        int previousRoundId = Sync.LastLiveRoundId;
        if (kingPlayerId < -1 || winnerId < -1
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        KingPlayerId = kingPlayerId;
        _subRoundId = roundId != previousRoundId
            ? subRoundId
            : Math.Max(_subRoundId, subRoundId);
        WinnerId = winnerId;
        WeaponsUnlocked = weaponsUnlocked;
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
        StartSubRound();
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Assassin)
            || WinnerId >= 0 || _subRoundEnding || !AlivePlayers.Remove(deadPlayerId))
        {
            return;
        }

        if (deadPlayerId == KingPlayerId)
        {
            AwardScore(AssassinPlayerId, PointsForAssassinWin);
            AnnounceResult("The King was killed. The Assassin was "
                + PlayerLookup.GetPlayerNameTag(AssassinPlayerId) + ".");
            FinishSubRound();
            return;
        }

        if (deadPlayerId == AssassinPlayerId)
        {
            AwardScore(KingPlayerId, PointsForKingSurvival);
            foreach (int playerId in Scores.Keys)
            {
                if (playerId != KingPlayerId && playerId != AssassinPlayerId)
                {
                    AwardScore(playerId, PointsForBodyguardSurvival);
                }
            }

            if (killerId >= 0 && killerId != deadPlayerId
                && killerId != KingPlayerId && killerId != AssassinPlayerId)
            {
                AwardScore(killerId, PointsForBodyguardKill);
            }

            AnnounceResult("The Assassin was "
                + PlayerLookup.GetPlayerNameTag(AssassinPlayerId) + " and was stopped.");
            FinishSubRound();
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

    internal static void ApplyLocalRole(CSteamID hostId, int subRoundId, bool isAssassin,
        bool isKing, bool announce)
    {
        if (hostId != MyceliumNetwork.LobbyHost || subRoundId < _localRoleSubRoundId)
        {
            return;
        }

        _localRoleSubRoundId = subRoundId;
        LocalIsAssassin = isAssassin;
        LocalIsKing = isKing;
        if (announce && GameModeManager.IsActive(GameMode.Assassin)
            && GameModeManager.Phase == GameModePhase.ActiveRound
            && !GameModeManager.IsMatchOver && _localRoleAnnouncedSubRoundId != subRoundId)
        {
            _localRoleAnnouncedSubRoundId = subRoundId;
            string roleText = isAssassin
                ? "You are the <color=#CC2222><b>ASSASSIN</b></color>."
                : isKing
                    ? "You are the <color=#35D05F><b>KING</b></color>."
                    : "You are a <color=#4D9BFF><b>BODYGUARD</b></color>.";
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

        if (players.Count < 2)
        {
            AssassinPlayerId = -1;
            KingPlayerId = -1;
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
                || WinnerId >= 0 || _subRoundEnding || !GameModeManager.IsActive(GameMode.Assassin))
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
            || !GameModeManager.IsActive(GameMode.Assassin))
        {
            yield break;
        }

        WeaponsUnlocked = true;
        EnsureLoadouts();
        BroadcastLiveState();
    }

    private static void FinishSubRound()
    {
        BroadcastLiveState();
        if (WinnerId >= 0)
        {
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(WinnerId));
            return;
        }

        BeginNextSubRound();
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

    private static IEnumerator StartNextSubRoundAfterRespawn(float delay, int sessionGeneration,
        int roundId)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.Assassin))
        {
            yield break;
        }

        StartSubRound();
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
            ApplyLocalRole(MyceliumNetwork.LobbyHost, _subRoundId,
                ClientInstance.Instance.PlayerId == AssassinPlayerId,
                ClientInstance.Instance.PlayerId == KingPlayerId, announce);
        }
    }

    private static void SendRoleToPlayer(CSteamID target, int playerId, bool announce)
    {
        if (playerId < 0 || target.m_SteamID == 0)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.AssassinModId, nameof(Plugin.SyncAssassinRole), target,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, _subRoundId,
            playerId == AssassinPlayerId, playerId == KingPlayerId, announce);
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

    private static bool SubRoundIsActive() => _subRoundId > 0 && !_subRoundEnding;

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
                WinnerId.ToString(), _subRoundId.ToString(), WeaponsUnlocked ? "1" : "0");
            MyceliumNetwork.RPC(Plugin.AssassinModId, nameof(Plugin.SyncAssassinLiveState),
                ReliableType.Reliable, MyceliumNetwork.LobbyHost, KingPlayerId, SerializeScores(),
                WinnerId, _subRoundId, WeaponsUnlocked, GameModeManager.RoundId, revision);
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
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 5, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[0], out int kingPlayerId)
            || !int.TryParse(fields[2], out int winnerId)
            || !int.TryParse(fields[3], out int subRoundId)
            || !LobbySnapshotCodec.TryParseBool(fields[4], out bool weaponsUnlocked))
        {
            return;
        }

        ApplyLiveState(hostId, kingPlayerId, fields[1], winnerId, subRoundId,
            weaponsUnlocked, roundId, revision, ModeLobbyDataSync.Source("assassin", "live"));
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
