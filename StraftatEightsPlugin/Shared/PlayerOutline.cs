using System;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class PlayerOutline
{
    private const string LiveLobbyDataKey = "StraftatEights_Outline_Live";
    private static readonly ModeSyncState Sync = new(livePushInterval: 1.5f);
    private static readonly List<SkinnedMeshRenderer> AppliedRenderers = new();
    private static readonly Dictionary<int, PlayerHealth> MultiTargets = new();
    private static readonly Color HvtColor = Color.blue;
    private static readonly Color JuggernautColor = new(1f, 0.42f, 0f);
    private static readonly Color KillTheRatColor = Color.yellow;
    private static readonly Color MichaelMeyersColor = Color.red;
    private static GameMode _lastMode = GameMode.None;
    private static GameMode _roleMode = GameMode.None;
    private static int _rolePlayerId = -1;
    private static GameMode _publishedMode = GameMode.None;
    private static int _publishedPlayerId = -1;
    private static int _publishedRoundId = -1;
    private static PlayerHealth? _singleTarget;

    internal static void Initialize()
    {
        ModeLobbyDataSync.RegisterKeys(LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += OnPlayerEntered;
    }

    internal static void ResetState()
    {
        ClearApplied();
        _lastMode = GameMode.None;
        _roleMode = GameMode.None;
        _rolePlayerId = -1;
        _publishedMode = GameMode.None;
        _publishedPlayerId = -1;
        _publishedRoundId = -1;
        _singleTarget = null;
        MultiTargets.Clear();
    }

    internal static void EnforceOutline()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            BroadcastRoleStateIfNeeded();
        }

        GameMode activeMode = GameModeManager.ActiveMode;
        if (UpdateMode(activeMode))
        {
            _singleTarget = null;
            MultiTargets.Clear();
        }

        if (GameModeManager.ShouldClearPlayerOutlines
            || !IsOutlineMode(activeMode) || _roleMode != activeMode)
        {
            ClearApplied();
            _singleTarget = null;
            MultiTargets.Clear();
            return;
        }

        if (activeMode == GameMode.MichaelMeyers)
        {
            EnforceMichaelMeyersOutline();
            return;
        }

        PlayerHealth? target = PlayerLookup.FindPlayerHealthById(_rolePlayerId);
        ApplySingleTarget(ref _singleTarget, target, GetColor(activeMode));
    }

    internal static bool UpdateMode(GameMode activeMode)
    {
        if (_lastMode == activeMode)
        {
            return false;
        }

        ClearAll();
        _lastMode = activeMode;
        return true;
    }

    internal static void ApplyRoleSnapshot(CSteamID hostId, int modeValue, int playerId,
        int roundId, int revision, string source = "rpc")
    {
        if (!Enum.IsDefined(typeof(GameMode), modeValue) || playerId < -1
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        GameMode mode = (GameMode)modeValue;
        SetRoleState(mode, playerId);
        Plugin.Logger.LogInfo($"[Outline] Accepted role state source={source} mode={mode} "
            + $"host={hostId.m_SteamID} round={roundId} revision={revision} target={playerId}");
    }

    internal static void ApplySingleTarget(ref PlayerHealth? current, PlayerHealth? target, Color color)
    {
        if (target == null || !target || !target.gameObject.activeInHierarchy)
        {
            ClearTarget(ref current);
            return;
        }

        if (current != target)
        {
            ClearApplied();
            current = target;
        }

        Apply(target, color);
    }

    internal static void ClearTarget(ref PlayerHealth? current)
    {
        if (current != null && current)
        {
            ClearApplied();
        }

        current = null;
    }

    private static bool IsOutlineMode(GameMode mode)
    {
        return mode == GameMode.HVT
            || mode == GameMode.Juggernaut
            || mode == GameMode.KillTheRat
            || mode == GameMode.MichaelMeyers;
    }

    private static Color GetColor(GameMode mode)
    {
        return mode switch
        {
            GameMode.HVT => HvtColor,
            GameMode.Juggernaut => JuggernautColor,
            GameMode.KillTheRat => KillTheRatColor,
            _ => MichaelMeyersColor
        };
    }

    private static void EnforceMichaelMeyersOutline()
    {
        int localPlayerId = ClientInstance.Instance == null ? -1 : ClientInstance.Instance.PlayerId;
        if (localPlayerId < 0 || _rolePlayerId != localPlayerId)
        {
            ClearApplied();
            MultiTargets.Clear();
            return;
        }

        Dictionary<int, PlayerHealth> currentTargets = new();
        foreach (int playerId in PlayerLookup.GetConnectedPlayerIds())
        {
            if (playerId == localPlayerId)
            {
                continue;
            }

            PlayerHealth? health = PlayerLookup.FindPlayerHealthById(playerId);
            if (health != null && health.gameObject.activeInHierarchy)
            {
                currentTargets[playerId] = health;
            }
        }

        if (!TargetsMatch(currentTargets))
        {
            ClearApplied();
            MultiTargets.Clear();
            foreach (KeyValuePair<int, PlayerHealth> target in currentTargets)
            {
                MultiTargets[target.Key] = target.Value;
            }
        }

        foreach (PlayerHealth health in MultiTargets.Values)
        {
            if (health != null && health.gameObject.activeInHierarchy)
            {
                Apply(health, MichaelMeyersColor);
            }
        }
    }

    private static bool TargetsMatch(Dictionary<int, PlayerHealth> currentTargets)
    {
        if (currentTargets.Count != MultiTargets.Count)
        {
            return false;
        }

        foreach (KeyValuePair<int, PlayerHealth> target in currentTargets)
        {
            if (!MultiTargets.TryGetValue(target.Key, out PlayerHealth outlined)
                || !ReferenceEquals(outlined, target.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static void SetRoleState(GameMode mode, int playerId)
    {
        if (_roleMode != mode || _rolePlayerId != playerId)
        {
            ClearApplied();
            _singleTarget = null;
            MultiTargets.Clear();
        }

        _roleMode = mode;
        _rolePlayerId = playerId;
    }

    private static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        _publishedMode = GameMode.None;
        _publishedPlayerId = -1;
        _publishedRoundId = -1;
        ResetState();

        if (MyceliumNetwork.IsHost)
        {
            BroadcastRoleStateIfNeeded(force: true);
        }
        else
        {
            ApplyLobbyLiveSnapshot();
        }
    }

    private static void OnLobbyLeft()
    {
        Sync.ResetForLobby();
        ResetState();
    }

    private static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        BroadcastRoleStateIfNeeded(force: true);
        MyceliumNetwork.RPCTarget(GameModeManager.ModId, nameof(Plugin.SyncOutlineRoleState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, (int)_publishedMode,
            _publishedPlayerId, _publishedRoundId, Sync.LiveRevision);
    }

    private static void OnLobbyDataUpdated(List<string> keys)
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !ModeLobbyDataSync.ContainsKey(keys, LiveLobbyDataKey))
        {
            return;
        }

        ApplyLobbyLiveSnapshot();
    }

    private static void BroadcastRoleStateIfNeeded(bool force = false)
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        GameMode mode = GameModeManager.ActiveMode;
        int playerId = GetRolePlayerId(mode);
        int roundId = GameModeManager.RoundId;
        bool changed = mode != _publishedMode || playerId != _publishedPlayerId
            || roundId != _publishedRoundId;
        bool due = Sync.IsLivePushDue();
        if (!force && !changed && !due)
        {
            return;
        }

        int revision = Sync.NextLiveRevision();
        _publishedMode = mode;
        _publishedPlayerId = playerId;
        _publishedRoundId = roundId;
        SetRoleState(mode, playerId);
        string payload = string.Join("|", MyceliumNetwork.LobbyHost.m_SteamID,
            (int)mode, playerId, roundId, revision);
        ModeLobbyDataSync.PublishRaw(LiveLobbyDataKey, payload);
        MyceliumNetwork.RPC(GameModeManager.ModId, nameof(Plugin.SyncOutlineRoleState),
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, (int)mode, playerId, roundId, revision);
    }

    private static int GetRolePlayerId(GameMode mode)
    {
        return mode switch
        {
            GameMode.HVT => HVTState.CurrentHVTPlayerId,
            GameMode.Juggernaut => JuggernautState.CurrentJuggernautPlayerId,
            GameMode.KillTheRat => KillTheRatState.CurrentRatPlayerId,
            GameMode.MichaelMeyers => MichaelMeyersState.CurrentMichaelPlayerId,
            _ => -1
        };
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryReadOrdered(LiveLobbyDataKey, 5, 0, 3, 4,
            out CSteamID hostId, out int roundId, out int revision, out string[] parts)
            || !int.TryParse(parts[1], out int mode)
            || !int.TryParse(parts[2], out int playerId))
        {
            return;
        }

        ApplyRoleSnapshot(hostId, mode, playerId, roundId, revision,
            ModeLobbyDataSync.Source("outline", "live"));
    }

    internal static void Apply(PlayerHealth player, Color color)
    {
        if (player == null)
        {
            return;
        }

        foreach (SkinnedMeshRenderer renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Material[] materials = renderer.materials;
            if (materials.Length == 0 || materials[0] == null || !materials[0].HasProperty("_ASEOutlineWidth"))
            {
                continue;
            }

            float outlineWidth = renderer.gameObject.name == "SM_Aboubi_Head00" ? 0.02f : 0.04f;
            if (!Mathf.Approximately(materials[0].GetFloat("_ASEOutlineWidth"), outlineWidth)
                || materials[0].GetColor("_ASEOutlineColor") != color)
            {
                materials[0].SetFloat("_ASEOutlineWidth", outlineWidth);
                materials[0].SetColor("_ASEOutlineColor", color);
                renderer.materials = materials;
            }

            if (!AppliedRenderers.Contains(renderer))
            {
                AppliedRenderers.Add(renderer);
            }
        }
    }

    internal static void Clear(PlayerHealth player)
    {
        if (player == null)
        {
            return;
        }

        foreach (SkinnedMeshRenderer renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            ClearRenderer(renderer.gameObject);
        }
    }

    internal static void ApplyTemporary(PlayerHealth player, Color color, float outlineWidth)
    {
        if (player == null)
        {
            return;
        }

        foreach (SkinnedMeshRenderer renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Material[] materials = renderer.materials;
            if (materials.Length == 0 || materials[0] == null || !materials[0].HasProperty("_ASEOutlineWidth"))
            {
                continue;
            }

            if (!Mathf.Approximately(materials[0].GetFloat("_ASEOutlineWidth"), outlineWidth)
                || materials[0].GetColor("_ASEOutlineColor") != color)
            {
                materials[0].SetFloat("_ASEOutlineWidth", outlineWidth);
                materials[0].SetColor("_ASEOutlineColor", color);
                renderer.materials = materials;
            }
        }
    }

    internal static void ClearTemporary(PlayerHealth player)
    {
        Clear(player);
    }

    internal static void ClearAll()
    {
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client)
            {
                continue;
            }

            PlayerManager? playerManager = client.PlayerSpawner;
            if (playerManager == null || !playerManager || playerManager.player == null || !playerManager.player)
            {
                continue;
            }

            PlayerHealth? player = playerManager.player.GetComponent<PlayerHealth>();
            if (player != null && player)
            {
                Clear(player);
            }
        }

        foreach (SkinnedMeshRenderer renderer in AppliedRenderers)
        {
            if (renderer != null)
            {
                ClearRenderer(renderer.gameObject);
            }
        }
        AppliedRenderers.Clear();
    }

    internal static void ClearApplied()
    {
        foreach (SkinnedMeshRenderer renderer in AppliedRenderers)
        {
            if (renderer != null)
            {
                ClearRenderer(renderer.gameObject);
            }
        }
        AppliedRenderers.Clear();
    }

    internal static bool HasAppliedRenderers => AppliedRenderers.Count > 0;

    private static void ClearRenderer(GameObject meshObject)
    {
        if (meshObject == null)
        {
            return;
        }

        SkinnedMeshRenderer? renderer = meshObject.GetComponent<SkinnedMeshRenderer>();
        if (renderer == null)
        {
            return;
        }

        Material[] materials = renderer.materials;
        if (materials.Length == 0 || materials[0] == null || !materials[0].HasProperty("_ASEOutlineWidth"))
        {
            return;
        }

        materials[0].SetFloat("_ASEOutlineWidth", 0f);
        renderer.materials = materials;
    }
}