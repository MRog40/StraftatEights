using System;
using System.Collections.Generic;
using System.Linq;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class HotPotInfectedState
{
    internal const string SettingsLobbyDataKey = "Eights_HotPotInfected_Settings";
    internal const string LiveLobbyDataKey = "Eights_HotPotInfected_Live";
    internal const string GrenadeWeaponName = "HandGrenade";
    internal const float InfectedSpeedMultiplier = 1.2f;
    internal static readonly float SurvivorHealth = HealthUnits.ToInternal(10f);
    internal static bool Enabled;
    internal static int InitialInfectedPlayerId { get; private set; } = -1;
    internal static int CurrentInfectedPlayerId => InitialInfectedPlayerId;
    internal static int WinnerId { get; private set; } = -1;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static readonly HashSet<int> InfectedPlayers = new();
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
        if (GameModeManager.ShouldDeferModeDisable(GameMode.HotPotInfected, enabled))
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

    private static void ApplySettingsFromHostConfig() =>
        ApplySettings(Plugin.HotPotInfectedEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        PublishSettingsSnapshot(revision);
        MyceliumNetwork.RPC(Plugin.HotPotInfectedModId,
            nameof(Plugin.SyncHotPotInfectedSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.HotPotInfectedEnabled.Value);
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
        if (Enabled && GameModeManager.IsActive(GameMode.HotPotInfected)
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

        MyceliumNetwork.RPCTarget(Plugin.HotPotInfectedModId,
            nameof(Plugin.SyncHotPotInfectedSettings), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.HotPotInfectedEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.HotPotInfectedModId,
            nameof(Plugin.SyncHotPotInfectedLiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, InitialInfectedPlayerId,
            SerializeInfectedPlayers(), SerializeScores(), WinnerId,
            GameModeManager.RoundId, Sync.LiveRevision);

        if (!GameModeManager.IsActive(GameMode.HotPotInfected) || !_rolesInitialized)
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
        bool wasInfected = playerId >= 0 && InfectedPlayers.Remove(playerId);
        bool wasRoundPlayer = playerId >= 0 && RoundPlayers.Remove(playerId);
        bool changed = wasInfected || wasRoundPlayer
            || (playerId >= 0 && Scores.Remove(playerId));
        RemoveLoadoutState(playerId);

        if (playerId >= 0 && InitialInfectedPlayerId == playerId)
        {
            InitialInfectedPlayerId = -1;
            int replacement = InfectedPlayers.Count == 0
                ? -1
                : InfectedPlayers.First();
            if (replacement < 0)
            {
                List<int> survivors = GetSurvivorIds();
                replacement = survivors.Count == 0 ? -1 : survivors[0];
                if (replacement >= 0)
                {
                    InfectedPlayers.Add(replacement);
                    MarkLoadoutDirty(replacement);
                }
            }

            InitialInfectedPlayerId = replacement;
            changed = true;
        }

        if (wasRoundPlayer && GameModeManager.IsActive(GameMode.HotPotInfected)
            && !_roundEnding && GetSurvivorIds().Count == 0)
        {
            FinishInfectedWin();
            return;
        }

        if (changed && GameModeManager.IsActive(GameMode.HotPotInfected))
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
        InitialInfectedPlayerId = -1;
        WinnerId = -1;
        InfectedPlayers.Clear();
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
        if (!Enabled || !GameModeManager.IsActive(GameMode.HotPotInfected)
            || !MyceliumNetwork.IsHost)
        {
            return;
        }

        Sync.ResetLiveState();
        InitialInfectedPlayerId = -1;
        WinnerId = -1;
        InfectedPlayers.Clear();
        RoundPlayers.Clear();
        PlayerObjectIds.Clear();
        AssignedWeapons.Clear();
        PendingLoadouts.Clear();
        ResolvedLoadouts.Clear();
        Scores.Clear();
        _rolesInitialized = false;
        _roundEnding = false;
        EnsureRoundRoles();
        BroadcastLiveState();
    }

    internal static void ApplyLiveState(CSteamID hostId, int initialInfectedPlayerId,
        string infectedData, string scoresData, int winnerId, int roundId, int revision,
        string source = "rpc")
    {
        if (initialInfectedPlayerId < -1 || winnerId < -1
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        InitialInfectedPlayerId = initialInfectedPlayerId;
        WinnerId = winnerId;
        InfectedPlayers.Clear();
        foreach (int playerId in ParseInfectedPlayers(infectedData))
        {
            InfectedPlayers.Add(playerId);
        }

        Scores.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(scoresData, PointsToWin))
        {
            Scores[entry.Key] = entry.Value;
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.HotPotInfected)
            || !MyceliumNetwork.IsHost || _roundEnding || WinnerId >= 0)
        {
            return;
        }

        EnsureRoundRoles();
        if (!RoundPlayers.Contains(deadPlayerId))
        {
            return;
        }

        if (InfectedRules.ShouldBecomeInfected(InfectedPlayers.Contains(deadPlayerId)))
        {
            InfectedPlayers.Add(deadPlayerId);
            MarkLoadoutDirty(deadPlayerId);
            GameModeHud.BroadcastAnnouncement(PlayerLookup.GetPlayerNameTag(deadPlayerId)
                + " became <color=#8B0000><b>INFECTED</b></color>.");
        }

        if (InfectedRules.ShouldEndRound(GetSurvivorIds().Count))
        {
            FinishInfectedWin();
            return;
        }

        BroadcastLiveState();
    }

    internal static void OnRoundTimeout()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.HotPotInfected)
            || !MyceliumNetwork.IsHost || _roundEnding || WinnerId >= 0)
        {
            return;
        }

        List<int> survivors = GetSurvivorIds();
        if (InfectedRules.ShouldEndRound(survivors.Count))
        {
            FinishInfectedWin();
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

    internal static bool IsInfected(int playerId)
    {
        return GameModeManager.IsActive(GameMode.HotPotInfected)
            && playerId >= 0 && InfectedPlayers.Contains(playerId);
    }

    internal static bool IsInfected(PlayerHealth health)
    {
        return health != null && IsInfected(
            health.playerValues?.playerClient?.PlayerId ?? -1);
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.HotPotInfected)
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

        InitialInfectedPlayerId = DistributionRandom.SelectPlayer("Hot Pot: Infected", players);
        if (InitialInfectedPlayerId >= 0)
        {
            InfectedPlayers.Add(InitialInfectedPlayerId);
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
            AssignedWeapons[playerId] = IsInfected(playerId)
                ? GrenadeWeaponName
                : GetRandomAllowedWeapon();
            PendingLoadouts[playerId] = Time.unscaledTime + 1f;
            ResolvedLoadouts.Remove(playerId);
        }

        if (ResolvedLoadouts.Contains(playerId) && !IsInfected(playerId))
        {
            return;
        }

        PlayerPickup? pickup = player.playerPickupScript;
        if (pickup == null || !pickup)
        {
            return;
        }

        bool infected = IsInfected(playerId);
        string weaponName = AssignedWeapons.TryGetValue(playerId, out string? assigned)
            ? assigned : infected ? GrenadeWeaponName : GetRandomAllowedWeapon();
        AssignedWeapons[playerId] = weaponName;

        if (infected)
        {
            Weapon? rightWeapon = GetHeldWeapon(pickup.objInHand);
            Weapon? leftWeapon = GetHeldWeapon(pickup.objInLeftHand);
            if (rightWeapon != null && IsHandGrenade(rightWeapon) && leftWeapon == null)
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
            WeaponService.GiveWeapon(playerId, GrenadeWeaponName);
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

        return WeaponSettingsState.Allowed[UnityEngine.Random.Range(0,
            WeaponSettingsState.Allowed.Count)];
    }

    private static bool IsHandGrenade(Weapon weapon)
    {
        return weapon.name.StartsWith(GrenadeWeaponName, StringComparison.Ordinal);
    }

    private static Weapon? GetHeldWeapon(GameObject? heldObject)
    {
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }

    private static void FinishInfectedWin()
    {
        if (_roundEnding || !InfectedRules.ShouldAwardInitialInfected(
                InitialInfectedPlayerId, GetSurvivorIds().Count))
        {
            return;
        }

        _roundEnding = true;
        AwardScore(InitialInfectedPlayerId, ScoreRules.PointsPerRoundWin);
        GameModeHud.BroadcastTakeResult("<b>The infected won</b>\n<i>All survivors were infected</i>");
        BroadcastLiveState();
        CompleteRound(new[] { InitialInfectedPlayerId },
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

    private static string SerializeInfectedPlayers()
    {
        return string.Join(";", InfectedPlayers.OrderBy(playerId => playerId));
    }

    private static List<int> ParseInfectedPlayers(string data)
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

        return InfectedRules.GetSurvivors(connectedPlayers, InfectedPlayers);
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
        MyceliumNetwork.RPC(Plugin.HotPotInfectedModId,
            nameof(Plugin.SyncHotPotInfectedLiveState), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, InitialInfectedPlayerId,
            SerializeInfectedPlayers(), SerializeScores(), WinnerId,
            GameModeManager.RoundId, revision);
    }

    private static void PublishSettingsSnapshot(int revision)
    {
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision,
            Plugin.HotPotInfectedEnabled.Value ? "1" : "0");
    }

    private static void PublishLiveSnapshot(int revision)
    {
        ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, InitialInfectedPlayerId.ToString(),
            SerializeInfectedPlayers(), SerializeScores(), WinnerId.ToString());
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("hot-pot-infected", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 4, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[0], out int initialInfectedPlayerId)
            || !int.TryParse(fields[3], out int winnerId))
        {
            return;
        }

        ApplyLiveState(hostId, initialInfectedPlayerId, fields[1], fields[2], winnerId,
            roundId, revision, ModeLobbyDataSync.Source("hot-pot-infected", "live"));
    }
}
