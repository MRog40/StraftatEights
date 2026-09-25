using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using MyceliumNetworking;
using UnityEngine;

namespace Eights;

internal static class TeamWeaponLoadouts
{
    private sealed class LoadoutRequest
    {
        internal LoadoutRequest(int playerObjectId, string weaponName)
        {
            PlayerObjectId = playerObjectId;
            WeaponName = weaponName;
        }

        internal int PlayerObjectId { get; }
        internal string WeaponName { get; }
        internal bool Complete { get; set; }
    }

    private static readonly Dictionary<int, int> InitialWeaponSlots = new();
    private static readonly Dictionary<int, int> PlayerObjectIds = new();
    private static readonly Dictionary<int, int> TeamRespawnCounts = new();
    private static readonly Dictionary<int, LoadoutRequest> LoadoutRequests = new();
    private static readonly List<string> AllowedSnapshot = new();
    private static TeamWeaponSequence? _weaponSequence;
    private static int _roundId = -1;
    private static int _assignmentVersion = -1;
    private static int _initialSlotCount;
    private static int _countertatTakeId = -1;
    private static float _nextLoadoutCheckTime;

    internal static void ResetMatchState()
    {
        InitialWeaponSlots.Clear();
        PlayerObjectIds.Clear();
        TeamRespawnCounts.Clear();
        LoadoutRequests.Clear();
        AllowedSnapshot.Clear();
        _weaponSequence = null;
        _roundId = -1;
        _assignmentVersion = -1;
        _initialSlotCount = 0;
        _countertatTakeId = -1;
        _nextLoadoutCheckTime = 0f;
    }

    internal static void EnsureLoadouts()
    {
        if (!MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || (!GameModeManager.IsTeamBased && !GameModeManager.IsActive(GameMode.Ffatat))
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || WeaponService.IsFinalGameScreen
            || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + 0.5f;

        bool countertat = GameModeManager.IsActive(GameMode.Countertat);
        if (countertat && SndtatState.TakeId < 1)
        {
            return;
        }
        if (!countertat && WeaponSettingsState.Allowed.Count == 0)
        {
            return;
        }

        EnsureRoundState();
        foreach (KeyValuePair<int, int> assignment in GetLoadoutAssignments())
        {
            if (!ClientInstance.playerInstances.TryGetValue(assignment.Key,
                out ClientInstance client)
                || client == null || !client
                || client.PlayerSpawner == null || !client.PlayerSpawner
                || client.PlayerSpawner.player == null || !client.PlayerSpawner.player)
            {
                continue;
            }

            EnsurePlayerLoadout(assignment.Key, assignment.Value, client.PlayerSpawner.player);
        }
    }

    internal static void OnPlayerSpawned(PlayerManager manager)
    {
        if (!MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || (!GameModeManager.IsTeamBased && !GameModeManager.IsActive(GameMode.Ffatat))
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || WeaponService.IsFinalGameScreen || manager == null || !manager
            || manager.player == null || !manager.player
            || (GameModeManager.IsActive(GameMode.Countertat)
                && SndtatState.TakeId < 1))
        {
            return;
        }

        ClientInstance? client = manager.GetComponent<ClientInstance>();
        if (client == null || !TryGetLoadoutAssignment(client.PlayerId, out int teamId))
        {
            return;
        }

        EnsureRoundState();
        EnsurePlayerLoadout(client.PlayerId, teamId, manager.player);
    }

    private static void EnsureRoundState()
    {
        if (_roundId != GameModeManager.RoundId)
        {
            ResetMatchState();
            _roundId = GameModeManager.RoundId;
        }

        bool countertat = GameModeManager.IsActive(GameMode.Countertat);
        if (!countertat
            && !AllowedSnapshot.SequenceEqual(WeaponSettingsState.Allowed, StringComparer.Ordinal))
        {
            AllowedSnapshot.Clear();
            AllowedSnapshot.AddRange(WeaponSettingsState.Allowed);
            InitialWeaponSlots.Clear();
            PlayerObjectIds.Clear();
            TeamRespawnCounts.Clear();
            LoadoutRequests.Clear();
            _weaponSequence = null;
            _assignmentVersion = -1;
            _initialSlotCount = 0;
        }

        if (countertat && _countertatTakeId != SndtatState.TakeId)
        {
            _countertatTakeId = SndtatState.TakeId;
            PlayerObjectIds.Clear();
            TeamRespawnCounts.Clear();
            LoadoutRequests.Clear();
        }

        if (_assignmentVersion == TeamAssignment.HealthCompensationVersion)
        {
            return;
        }

        Dictionary<int, List<int>> playersByTeam = new();
        foreach (KeyValuePair<int, int> assignment in GetLoadoutAssignments())
        {
            if (!playersByTeam.TryGetValue(assignment.Value, out List<int>? players))
            {
                players = new List<int>();
                playersByTeam[assignment.Value] = players;
            }
            players.Add(assignment.Key);
        }

        foreach (List<int> players in playersByTeam.Values)
        {
            players.Sort();
            int nextSlot = 0;
            foreach (int playerId in players)
            {
                if (InitialWeaponSlots.ContainsKey(playerId))
                {
                    nextSlot = Math.Max(nextSlot, InitialWeaponSlots[playerId] + 1);
                    continue;
                }

                InitialWeaponSlots[playerId] = nextSlot++;
            }
            _initialSlotCount = Math.Max(_initialSlotCount, nextSlot);
        }

        _assignmentVersion = TeamAssignment.HealthCompensationVersion;
    }

    private static IEnumerable<KeyValuePair<int, int>> GetLoadoutAssignments()
    {
        if (GameModeManager.IsActive(GameMode.Ffatat))
        {
            foreach (int playerId in PlayerLookup.GetConnectedPlayerIdsReadOnly())
            {
                yield return new KeyValuePair<int, int>(playerId, playerId);
            }

            yield break;
        }

        foreach (KeyValuePair<int, int> assignment in TeamAssignment.Current
            .OrderBy(entry => entry.Key))
        {
            yield return assignment;
        }
    }

    private static bool TryGetLoadoutAssignment(int playerId, out int teamId)
    {
        if (GameModeManager.IsActive(GameMode.Ffatat))
        {
            teamId = playerId;
            return true;
        }

        return TeamAssignment.Current.TryGetValue(playerId, out teamId);
    }

    private static void EnsurePlayerLoadout(int playerId, int teamId,
        FirstPersonController player)
    {
        int playerObjectId = player.GetInstanceID();
        if (!PlayerObjectIds.TryGetValue(playerId, out int previousObjectId)
            || previousObjectId != playerObjectId)
        {
            string weaponName;
            if (GameModeManager.IsActive(GameMode.Countertat))
            {
                if (!InitialWeaponSlots.TryGetValue(playerId, out int playerSlot))
                {
                    playerSlot = _initialSlotCount++;
                    InitialWeaponSlots[playerId] = playerSlot;
                }

                weaponName = CountertatRules.GetWeaponName(SndtatState.OffensiveTeamId,
                    SndtatState.TakeId, teamId, playerSlot);
            }
            else
            {
                bool isRespawn = PlayerObjectIds.ContainsKey(playerId);
                int weaponIndex;
                if (isRespawn)
                {
                    TeamRespawnCounts.TryGetValue(teamId, out int respawnCount);
                    respawnCount++;
                    TeamRespawnCounts[teamId] = respawnCount;
                    weaponIndex = _initialSlotCount + respawnCount - 1;
                }
                else
                {
                    weaponIndex = InitialWeaponSlots.TryGetValue(playerId, out int initialSlot)
                        ? initialSlot
                        : _initialSlotCount;
                    if (!InitialWeaponSlots.ContainsKey(playerId))
                    {
                        InitialWeaponSlots[playerId] = weaponIndex;
                        _initialSlotCount++;
                    }
                }

                weaponName = GetWeaponAt(weaponIndex);
            }

            PlayerObjectIds[playerId] = playerObjectId;
            LoadoutRequests[playerId] = new LoadoutRequest(playerObjectId, weaponName);
        }

        if (!LoadoutRequests.TryGetValue(playerId, out LoadoutRequest? request)
            || request.PlayerObjectId != playerObjectId)
        {
            return;
        }

        PlayerPickup? pickup = player.playerPickupScript;
        Weapon? heldWeapon = pickup == null || !pickup
            ? null
            : GetHeldWeapon(pickup);
        if (heldWeapon != null
            && heldWeapon.name.StartsWith(request.WeaponName, StringComparison.Ordinal))
        {
            if (!request.Complete)
            {
                WeaponAmmoTuning.InitializeFromSpawnerPickup(heldWeapon,
                    WeaponSettingsState.SpareMagazines);
            }
            request.Complete = true;
            return;
        }

        if (pickup == null || !pickup)
        {
            return;
        }

        NetworkObject? pickupNetworkObject = pickup.GetComponent<NetworkObject>();
        if (request.Complete || !pickup.IsServer
            || pickupNetworkObject == null || !pickupNetworkObject.IsSpawned)
        {
            return;
        }

        request.Complete = true;
        WeaponService.GiveWeapon(playerId, request.WeaponName,
            WeaponSettingsState.SpareMagazines);
    }

    private static Weapon? GetHeldWeapon(PlayerPickup pickup)
    {
        GameObject? heldObject = pickup.objInHand;
        return heldObject == null || !heldObject
            ? null
            : heldObject.GetComponent<Weapon>();
    }

    private static string GetWeaponAt(int index)
    {
        _weaponSequence ??= new TeamWeaponSequence(WeaponSettingsState.Allowed,
            UnityEngine.Random.Range(0, int.MaxValue));
        return _weaponSequence.GetAt(index);
    }
}
