using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using MyceliumNetworking;
using UnityEngine;

namespace StraftatEightsPlugin;

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
    }

    internal static void EnsureLoadouts()
    {
        if (!MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !GameModeManager.IsTeamBased
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || WeaponService.IsFinalGameScreen)
        {
            return;
        }

        if (WeaponSettingsState.Allowed.Count == 0)
        {
            DebugLog.Every("team-weapons-empty", 10f,
                $"[TeamWeapons] No allowed weapons for mode={GameModeManager.ActiveMode}");
            return;
        }

        EnsureRoundState();
        foreach (KeyValuePair<int, int> assignment in TeamAssignment.Current.OrderBy(entry => entry.Key))
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

    private static void EnsureRoundState()
    {
        if (_roundId != GameModeManager.RoundId)
        {
            ResetMatchState();
            _roundId = GameModeManager.RoundId;
        }

        if (!AllowedSnapshot.SequenceEqual(WeaponSettingsState.Allowed, StringComparer.Ordinal))
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

        if (_assignmentVersion == TeamAssignment.HealthCompensationVersion)
        {
            return;
        }

        Dictionary<int, List<int>> playersByTeam = new();
        foreach (KeyValuePair<int, int> assignment in TeamAssignment.Current)
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

    private static void EnsurePlayerLoadout(int playerId, int teamId,
        FirstPersonController player)
    {
        int playerObjectId = player.GetInstanceID();
        if (!PlayerObjectIds.TryGetValue(playerId, out int previousObjectId)
            || previousObjectId != playerObjectId)
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

            string weaponName = GetWeaponAt(weaponIndex);
            PlayerObjectIds[playerId] = playerObjectId;
            LoadoutRequests[playerId] = new LoadoutRequest(playerObjectId, weaponName);
            DebugLog.Info($"[TeamWeapons] Assigned player={playerId} team={teamId} "
                + $"weapon={weaponName} index={weaponIndex} respawn={isRespawn}");
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
            WeaponAmmoTuning.InitializeFromSpawnerPickup(heldWeapon,
                WeaponSettingsState.SpareMagazines);
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
