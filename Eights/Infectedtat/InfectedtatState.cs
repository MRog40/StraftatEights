using System;
using System.Collections.Generic;
using System.Linq;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class InfectedtatState
{
    internal const string SettingsLobbyDataKey = "Eights_Infectedtat_Settings";
    internal const string LiveLobbyDataKey = "Eights_Infectedtat_Live";
    internal const string KnifeWeaponName = "Couperet";
    internal const float InfectedtatSpeedMultiplier = 1.2f;
    internal const float SurvivorHealth = 10f / 25f;
    internal static bool Enabled;
    internal static int InitialInfectedtatPlayerId { get; private set; } = -1;
    internal static int CurrentInfectedtatPlayerId => InitialInfectedtatPlayerId;
    internal static int WinnerId { get; private set; } = -1;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static readonly HashSet<int> InfectedtatPlayers = new();
    internal static readonly Dictionary<int, int> Scores = new();

    private static readonly HashSet<int> RoundPlayers = new();
    private static readonly Dictionary<int, int> PlayerObjectIds = new();
    private static readonly Dictionary<int, string> AssignedWeapons = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static readonly HashSet<int> ResolvedLoadouts = new();
    private static readonly ModeSyncState Sync = new(livePushInterval: 1f);
    private static bool _rolesInitialized;
    private static bool _roundEnding;

    internal static int SurvivorCount => GetSurvivorIds().Count;
    internal static bool IsRoundEnding => _roundEnding || WinnerId >= 0
        || GameModeManager.Phase == GameModePhase.EndingRound;

    internal static void ApplySettings(bool enabled)
    {
        if (GameModeManager.ShouldDeferModeDisable(GameMode.Infectedtat, enabled))
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

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.InfectedtatEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        PublishSettingsSnapshot(revision);
        MyceliumNetwork.RPC(Plugin.InfectedtatModId, nameof(Plugin.SyncInfectedtatSettings),
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId,
            revision, Plugin.InfectedtatEnabled.Value);
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
        PeriodicPushSettingsIfHost();
        if (Enabled && GameModeManager.IsActive(GameMode.Infectedtat)
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

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.InfectedtatModId,
            nameof(Plugin.SyncInfectedtatSettings), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.InfectedtatEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.InfectedtatModId,
            nameof(Plugin.SyncInfectedtatLiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, InitialInfectedtatPlayerId,
            SerializeInfectedtatPlayers(), SerializeScores(), WinnerId,
            GameModeManager.RoundId, Sync.LiveRevision);

        if (!GameModeManager.IsActive(GameMode.Infectedtat) || !_rolesInitialized)
        {
            return;
        }

        int playerId = PlayerLookup.FindPlayerId(player);
        if (playerId < 0 || !RoundPlayers.Add(playerId))
        {
            return;
        }

        Scores.TryAdd(playerId, 0);
        BroadcastLiveState();
    }

    internal static void OnPlayerLeft(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        int playerId = PlayerLookup.FindPlayerId(player);
        bool wasInfectedtat = playerId >= 0 && InfectedtatPlayers.Remove(playerId);
        bool wasRoundPlayer = playerId >= 0 && RoundPlayers.Remove(playerId);
        bool changed = wasInfectedtat || wasRoundPlayer
            || (playerId >= 0 && Scores.Remove(playerId));
        RemoveLoadoutState(playerId);

        if (playerId >= 0 && InitialInfectedtatPlayerId == playerId)
        {
            InitialInfectedtatPlayerId = -1;
            int replacement = InfectedtatPlayers.Count == 0
                ? -1
                : InfectedtatPlayers.First();
            if (replacement < 0)
            {
                List<int> survivors = GetSurvivorIds();
                replacement = survivors.Count == 0 ? -1 : survivors[0];
                if (replacement >= 0)
                {
                    InfectedtatPlayers.Add(replacement);
                    MarkLoadoutDirty(replacement);
                }
            }

            InitialInfectedtatPlayerId = replacement;
            changed = true;
        }

        if (wasRoundPlayer && GameModeManager.IsActive(GameMode.Infectedtat)
            && !_roundEnding && GetSurvivorIds().Count == 0)
        {
            FinishInfectedtatWin();
            return;
        }

        if (changed && GameModeManager.IsActive(GameMode.Infectedtat))
        {
            BroadcastLiveState();
        }
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId,
        int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        InitialInfectedtatPlayerId = -1;
        WinnerId = -1;
        InfectedtatPlayers.Clear();
        RoundPlayers.Clear();
        PlayerObjectIds.Clear();
        AssignedWeapons.Clear();
        PendingLoadouts.Clear();
        ResolvedLoadouts.Clear();
        Scores.Clear();
        _rolesInitialized = false;
        _roundEnding = false;
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infectedtat)
            || !MyceliumNetwork.IsHost)
        {
            return;
        }

        Sync.ResetLiveState();
        InitialInfectedtatPlayerId = -1;
        WinnerId = -1;
        InfectedtatPlayers.Clear();
        RoundPlayers.Clear();
        PlayerObjectIds.Clear();
        AssignedWeapons.Clear();
        PendingLoadouts.Clear();
        ResolvedLoadouts.Clear();
        _rolesInitialized = false;
        _roundEnding = false;
        EnsureRoundRoles();
        BroadcastLiveState();
    }

    internal static void ApplyLiveState(CSteamID hostId, int initialInfectedtatPlayerId,
        string infectedData, string scoresData, int winnerId, int roundId, int revision,
        string source = "rpc")
    {
        if (initialInfectedtatPlayerId < -1 || winnerId < -1
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        InitialInfectedtatPlayerId = initialInfectedtatPlayerId;
        WinnerId = winnerId;
        InfectedtatPlayers.Clear();
        foreach (int playerId in ParseInfectedtatPlayers(infectedData))
        {
            InfectedtatPlayers.Add(playerId);
        }

        Scores.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(scoresData, PointsToWin))
        {
            Scores[entry.Key] = entry.Value;
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infectedtat)
            || !MyceliumNetwork.IsHost || _roundEnding || WinnerId >= 0)
        {
            return;
        }

        EnsureRoundRoles();
        if (!RoundPlayers.Contains(deadPlayerId))
        {
            return;
        }

        if (InfectedtatRules.ShouldBecomeInfectedtat(InfectedtatPlayers.Contains(deadPlayerId)))
        {
            InfectedtatPlayers.Add(deadPlayerId);
            MarkLoadoutDirty(deadPlayerId);
            GameModeHud.BroadcastAnnouncement(PlayerLookup.GetPlayerNameTag(deadPlayerId)
                + " became <color=#8B0000><b>INFECTED</b></color>.");
        }

        if (InfectedtatRules.ShouldEndRound(GetSurvivorIds().Count))
        {
            FinishInfectedtatWin();
            return;
        }

        BroadcastLiveState();
    }

    internal static void OnRoundTimeout()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infectedtat)
            || !MyceliumNetwork.IsHost || _roundEnding || WinnerId >= 0)
        {
            return;
        }

        List<int> survivors = GetSurvivorIds();
        if (InfectedtatRules.ShouldEndRound(survivors.Count))
        {
            FinishInfectedtatWin();
            return;
        }

        foreach (int playerId in survivors)
        {
            AwardScore(playerId, ScoreRules.PointsPerRoundWin);
        }

        GameModeHud.BroadcastTakeResult("<b>The survivors won</b>\n<i>They survived the round</i>");
        BroadcastLiveState();
        CompleteRound(survivors, "The survivors survived the timer and won the round.");
    }

    internal static bool IsInfectedtat(int playerId)
    {
        return GameModeManager.IsActive(GameMode.Infectedtat)
            && playerId >= 0 && InfectedtatPlayers.Contains(playerId);
    }

    internal static bool IsInfectedtat(PlayerHealth health)
    {
        return health != null && IsInfectedtat(
            health.playerValues?.playerClient?.PlayerId ?? -1);
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infectedtat)
            || !MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || WeaponService.IsFinalGameScreen)
        {
            return;
        }

        EnsureRoundRoles();
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client || client.PlayerSpawner == null
                || !client.PlayerSpawner || client.PlayerSpawner.player == null
                || !client.PlayerSpawner.player)
            {
                continue;
            }

            EnsurePlayerLoadout(client.PlayerId, client.PlayerSpawner.player);
        }
    }

    internal static void MarkLoadoutDirty(int playerId)
    {
        RemoveLoadoutState(playerId);
    }

    private static void EnsureRoundRoles()
    {
        if (_rolesInitialized)
        {
            return;
        }

        List<int> players = PlayerLookup.GetConnectedPlayerIds();
        if (players.Count == 0)
        {
            return;
        }

        RoundPlayers.Clear();
        foreach (int playerId in players)
        {
            RoundPlayers.Add(playerId);
            Scores.TryAdd(playerId, 0);
        }

        InitialInfectedtatPlayerId = DistributionRandom.SelectPlayer("Infectedtat", players);
        if (InitialInfectedtatPlayerId >= 0)
        {
            InfectedtatPlayers.Add(InitialInfectedtatPlayerId);
        }
        _rolesInitialized = true;
    }

    private static void EnsurePlayerLoadout(int playerId, FirstPersonController player)
    {
        if (!_rolesInitialized || !RoundPlayers.Contains(playerId))
        {
            return;
        }

        int objectId = player.GetInstanceID();
        if (!PlayerObjectIds.TryGetValue(playerId, out int previousObjectId)
            || previousObjectId != objectId)
        {
            PlayerObjectIds[playerId] = objectId;
            AssignedWeapons[playerId] = IsInfectedtat(playerId)
                ? KnifeWeaponName
                : GetRandomAllowedWeapon();
            PendingLoadouts[playerId] = Time.unscaledTime + 1f;
            ResolvedLoadouts.Remove(playerId);
        }

        if (ResolvedLoadouts.Contains(playerId))
        {
            return;
        }

        PlayerPickup? pickup = player.playerPickupScript;
        if (pickup == null || !pickup)
        {
            return;
        }

        bool infected = IsInfectedtat(playerId);
        string weaponName = AssignedWeapons.TryGetValue(playerId, out string? assigned)
            ? assigned : infected ? KnifeWeaponName : GetRandomAllowedWeapon();
        AssignedWeapons[playerId] = weaponName;

        if (infected)
        {
            Weapon? rightWeapon = GetHeldWeapon(pickup.objInHand);
            Weapon? leftWeapon = GetHeldWeapon(pickup.objInLeftHand);
            if (rightWeapon != null && IsCouperet(rightWeapon) && leftWeapon == null)
            {
                ResolvedLoadouts.Add(playerId);
                PendingLoadouts.Remove(playerId);
                return;
            }
        }
        else if (weaponName.Length == 0)
        {
            ResolvedLoadouts.Add(playerId);
            PendingLoadouts.Remove(playerId);
            return;
        }
        else
        {
            Weapon? heldWeapon = GetHeldWeapon(pickup.objInHand);
            if (heldWeapon != null && heldWeapon.name.StartsWith(weaponName,
                    StringComparison.Ordinal))
            {
                ResolvedLoadouts.Add(playerId);
                PendingLoadouts.Remove(playerId);
                return;
            }
        }

        if (PendingLoadouts.TryGetValue(playerId, out float retryTime)
            && Time.unscaledTime < retryTime)
        {
            return;
        }

        PendingLoadouts[playerId] = Time.unscaledTime + 1f;
        if (infected)
        {
            WeaponService.GiveWeapon(playerId, KnifeWeaponName);
        }
        else
        {
            WeaponService.GiveWeapon(playerId, weaponName,
                WeaponSettingsState.SpareMagazines);
        }
    }

    private static string GetRandomAllowedWeapon()
    {
        if (!WeaponSettingsState.Enabled || WeaponSettingsState.Allowed.Count == 0)
        {
            return string.Empty;
        }

        return WeaponSettingsState.Allowed[UnityEngine.Random.Range(0, WeaponSettingsState.Allowed.Count)];
    }

    private static Weapon? GetHeldWeapon(GameObject? heldObject)
    {
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }

    private static bool IsCouperet(Weapon weapon)
    {
        return weapon.name.StartsWith(KnifeWeaponName, StringComparison.Ordinal);
    }

    private static void FinishInfectedtatWin()
    {
        if (_roundEnding || !InfectedtatRules.ShouldAwardInitialInfectedtat(
                InitialInfectedtatPlayerId, GetSurvivorIds().Count))
        {
            return;
        }

        _roundEnding = true;
        AwardScore(InitialInfectedtatPlayerId, ScoreRules.PointsPerRoundWin);
        GameModeHud.BroadcastTakeResult("<b>The infected won</b>\n<i>All survivors were infected</i>");
        BroadcastLiveState();
        CompleteRound(new[] { InitialInfectedtatPlayerId },
            "The infected eliminated every survivor and won the round.");
    }

    private static void CompleteRound(IReadOnlyList<int> winners, string announcement)
    {
        if (winners.Count == 0)
        {
            return;
        }

        GameModeHud.BroadcastAnnouncement(announcement);
        List<int> winningTeams = winners.Select(TeamAssignment.ResolveTeamId)
            .Where(teamId => teamId >= 0).Distinct().ToList();
        if (winningTeams.Count > 0)
        {
            GameModeManager.CompleteCustomRound(winningTeams);
        }
    }

    private static void AwardScore(int playerId, int amount)
    {
        if (playerId < 0 || amount <= 0)
        {
            return;
        }

        Scores.TryGetValue(playerId, out int currentScore);
        int nextScore = Math.Min(PointsToWin, currentScore + amount);
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

    private static string SerializeScores() => ScoreCodec.Serialize(Scores);

    private static string SerializeInfectedtatPlayers()
    {
        return string.Join(";", InfectedtatPlayers.OrderBy(playerId => playerId));
    }

    private static List<int> ParseInfectedtatPlayers(string data)
    {
        List<int> players = new();
        foreach (string value in (data ?? string.Empty).Split(';'))
        {
            if (int.TryParse(value, out int playerId) && playerId >= 0)
            {
                players.Add(playerId);
            }
        }

        return players;
    }

    private static List<int> GetSurvivorIds()
    {
        List<int> connectedPlayers = new();
        foreach (int playerId in RoundPlayers)
        {
            if (ClientInstance.playerInstances.ContainsKey(playerId))
            {
                connectedPlayers.Add(playerId);
            }
        }

        return InfectedtatRules.GetSurvivors(connectedPlayers, InfectedtatPlayers);
    }

    private static void RemoveLoadoutState(int playerId)
    {
        if (playerId < 0)
        {
            return;
        }

        PlayerObjectIds.Remove(playerId);
        AssignedWeapons.Remove(playerId);
        PendingLoadouts.Remove(playerId);
        ResolvedLoadouts.Remove(playerId);
    }

    private static void BroadcastLiveState()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        int revision = Sync.NextLiveRevision();
        PublishLiveSnapshot(revision);
        MyceliumNetwork.RPC(Plugin.InfectedtatModId, nameof(Plugin.SyncInfectedtatLiveState),
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, InitialInfectedtatPlayerId,
            SerializeInfectedtatPlayers(), SerializeScores(), WinnerId,
            GameModeManager.RoundId, revision);
    }

    private static void PublishSettingsSnapshot(int revision)
    {
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.InfectedtatEnabled.Value ? "1" : "0");
    }

    private static void PublishLiveSnapshot(int revision)
    {
        ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, InitialInfectedtatPlayerId.ToString(),
            SerializeInfectedtatPlayers(), SerializeScores(), WinnerId.ToString());
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("infectedtat", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 4, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[0], out int initialInfectedtatPlayerId)
            || !int.TryParse(fields[3], out int winnerId))
        {
            return;
        }

        ApplyLiveState(hostId, initialInfectedtatPlayerId, fields[1], fields[2], winnerId,
            roundId, revision, ModeLobbyDataSync.Source("infectedtat", "live"));
    }
}