using System.Collections;
using MyceliumNetworking;
using Steamworks;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using FishNet;
using FishNetSceneLoadData = FishNet.Managing.Scened.SceneLoadData;
using FishNetReplaceOption = FishNet.Managing.Scened.ReplaceOption;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StraftatEightsPlugin;

internal enum GameMode
{
    None = 0,
    FreeForAll = 1,
    Juggernaut = 2,
    GunGame = 3,
    SniperBattle = 4,
    Default = 5,
    MichaelMeyers = 6,
    KillTheRat = 7,
    OneInTheChamber = 8,
    HotPotato = 9,
    Infidel = 10,
    HVT = 11,
    Assassin = 12,
    Hardpoint = 13
}

internal enum GameModePhase
{
    Inactive,
    Lobby,
    ActiveRound,
    EndingRound
}

[Flags]
internal enum GameModeCapabilities
{
    None = 0,
    CustomRound = 1,
    IgnoreGlobalWeapons = 2,
    IgnoreGlobalHealth = 4,
    HideHud = 8,
    ClearOutlines = 16,
    IgnoreGlobalMovement = 32,
    SafeRespawn = 64,
    TeamBased = 128
}

internal static class GameModeManager
{
    internal const uint ModId = 1618033988u;
    internal const string ActiveModeLobbyDataKey = "StraftatEights_ActiveMode";
    private sealed class ModeDescriptor
    {
        internal readonly string Label;
        internal readonly Color Color;
        internal readonly Func<bool> IsEnabled;
        internal readonly Action Reset;
        internal readonly GameModeCapabilities Capabilities;
        internal readonly Action PeriodicSettingsPush;
        internal readonly Action PeriodicPush;
        internal readonly Action EnsureLoadouts;

        internal ModeDescriptor(string label, Color color, Func<bool> isEnabled, Action reset,
            GameModeCapabilities capabilities, Action? periodicPush = null, Action? ensureLoadouts = null,
            Action? periodicSettingsPush = null)
        {
            Label = label;
            Color = color;
            IsEnabled = isEnabled;
            Reset = reset;
            Capabilities = capabilities;
            PeriodicSettingsPush = periodicSettingsPush ?? periodicPush ?? Noop;
            PeriodicPush = periodicPush ?? Noop;
            EnsureLoadouts = ensureLoadouts ?? Noop;
        }
    }

    private static readonly GameMode[] ModeOrder =
    {
        GameMode.Default,
        GameMode.FreeForAll,
        GameMode.Juggernaut,
        GameMode.GunGame,
        GameMode.SniperBattle,
        GameMode.MichaelMeyers,
        GameMode.KillTheRat,
        GameMode.OneInTheChamber,
        GameMode.HotPotato,
        GameMode.Infidel,
        GameMode.HVT,
        GameMode.Assassin,
        GameMode.Hardpoint
    };

    private static readonly Dictionary<GameMode, ModeDescriptor> Modes = new()
    {
        [GameMode.Default] = new ModeDescriptor("DEFAULT", new Color32(220, 220, 220, 255),
            () => Plugin.DefaultGameModeEnabled.Value, DefaultReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.IgnoreGlobalMovement
            | GameModeCapabilities.SafeRespawn,
            DefaultGameModeState.PeriodicPushIfHost),
        [GameMode.FreeForAll] = new ModeDescriptor("FFA", new Color32(85, 204, 255, 255),
            () => Plugin.FFAEnabled.Value, FfaReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.SafeRespawn,
               FFAState.PeriodicPushIfHost, periodicSettingsPush: FFAState.PeriodicPushSettingsIfHost),
        [GameMode.Juggernaut] = new ModeDescriptor("JUGGERNAUT", new Color32(255, 106, 0, 255),
            () => Plugin.JuggernautEnabled.Value, JuggernautReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.SafeRespawn,
            JuggernautState.PeriodicPushIfHost, JuggernautState.EnsureLoadout,
            JuggernautState.PeriodicPushSettingsIfHost),
        [GameMode.GunGame] = new ModeDescriptor("GUN GAME", new Color32(255, 221, 85, 255),
            () => Plugin.GunGameEnabled.Value, GunGameReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
               GunGameState.PeriodicPushIfHost, GunGameState.EnsureLoadouts,
               periodicSettingsPush: GunGameState.PeriodicPushSettingsIfHost),
        [GameMode.SniperBattle] = new ModeDescriptor("SNIPER BATTLE", new Color32(255, 96, 128, 255),
            () => Plugin.SniperBattleEnabled.Value, SniperBattleReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.ClearOutlines
            | GameModeCapabilities.SafeRespawn,
               SniperBattleState.PeriodicPushIfHost, SniperBattleState.EnsureLoadouts,
               SniperBattleState.PeriodicPushSettingsIfHost),
        [GameMode.MichaelMeyers] = new ModeDescriptor("MICHAEL MEYERS", new Color32(204, 34, 34, 255),
            () => Plugin.MichaelMeyersEnabled.Value, MichaelMeyersReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.HideHud,
            MichaelMeyersPeriodicPush, MichaelMeyersState.EnsureLoadouts,
            MichaelMeyersState.PeriodicPushSettingsIfHost),
        [GameMode.KillTheRat] = new ModeDescriptor("EXTERMINATORS", new Color32(170, 170, 170, 255),
            () => Plugin.KillTheRatEnabled.Value, KillTheRatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
            KillTheRatState.PeriodicPushIfHost, KillTheRatState.EnsureLoadouts,
            KillTheRatState.PeriodicPushSettingsIfHost),
        [GameMode.OneInTheChamber] = new ModeDescriptor("ONE IN THE CHAMBER", new Color32(180, 180, 180, 255),
            () => Plugin.OneInTheChamberEnabled.Value, OneInTheChamberReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.SafeRespawn,
            OneInTheChamberState.PeriodicPushIfHost, OneInTheChamberState.EnsureLoadouts,
            OneInTheChamberState.PeriodicPushSettingsIfHost),
        [GameMode.HotPotato] = new ModeDescriptor("HOT POTATO", new Color32(255, 170, 70, 255),
            () => Plugin.HotPotatoEnabled.Value, HotPotatoReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
            HotPotatoState.PeriodicPushIfHost, HotPotatoState.EnsureLoadouts,
            HotPotatoState.PeriodicPushSettingsIfHost),
        [GameMode.Infidel] = new ModeDescriptor("INFIDEL", new Color32(204, 64, 64, 255),
            () => Plugin.InfidelEnabled.Value, InfidelReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.SafeRespawn,
            InfidelState.PeriodicPushIfHost, InfidelState.EnsureLoadouts,
            InfidelState.PeriodicPushSettingsIfHost),
        [GameMode.HVT] = new ModeDescriptor("HVT", new Color32(0, 0, 255, 255),
            () => Plugin.HVTEnabled.Value, HVTReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.SafeRespawn,
            HVTState.PeriodicPushIfHost, periodicSettingsPush: HVTState.PeriodicPushSettingsIfHost),
        [GameMode.Assassin] = new ModeDescriptor("ASSASSIN", new Color32(53, 208, 95, 255),
            () => Plugin.AssassinEnabled.Value, AssassinReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
            AssassinState.PeriodicPushIfHost, AssassinState.EnsureLoadouts,
            AssassinState.PeriodicPushSettingsIfHost),
        [GameMode.Hardpoint] = new ModeDescriptor("HARDPOINT", new Color32(0, 114, 178, 255),
            () => Plugin.HardpointEnabled.Value, HardpointReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            HardpointState.PeriodicPushIfHost, HardpointState.EnsureLoadouts,
            HardpointState.PeriodicPushSettingsIfHost)
    };

    private static void DefaultReset() => DefaultGameModeState.ResetMatchState();
    private static void Noop() { }
    private static void MichaelMeyersPeriodicPush()
    {
        MichaelMeyersState.PeriodicPushSettingsIfHost();
        MichaelMeyersState.PeriodicPushLiveStateIfHost();
    }
    private static void FfaReset() => FFAState.ResetMatchState();
    private static void JuggernautReset() => JuggernautState.ResetMatchState();
    private static void GunGameReset() => GunGameState.ResetMatchState();
    private static void SniperBattleReset() => SniperBattleState.ResetMatchState();
    private static void MichaelMeyersReset() => MichaelMeyersState.ResetMatchState();
    private static void KillTheRatReset() => KillTheRatState.ResetMatchState();
    private static void OneInTheChamberReset() => OneInTheChamberState.ResetMatchState();
    private static void HotPotatoReset() => HotPotatoState.ResetMatchState();
    private static void InfidelReset() => InfidelState.ResetMatchState();
    private static void HVTReset() => HVTState.ResetMatchState();
    private static void AssassinReset() => AssassinState.ResetMatchState();
    private static void HardpointReset() => HardpointState.ResetMatchState();

    internal static GameMode ActiveMode { get; private set; }
    internal static GameModePhase Phase { get; private set; } = GameModePhase.Inactive;
    internal static int RoundId { get; private set; }
    internal static string SelectedMapName { get; private set; } = string.Empty;
    internal static ConfigEntry<float> RespawnDelaySeconds = null!;
    internal static ConfigEntry<int> PointsToWin = null!;
    internal static float EffectiveRespawnDelaySeconds { get; set; } = 3f;
    internal static int EffectivePointsToWin { get; private set; } = ScoreRules.PointsToWin;
    private static readonly ModeSyncState Sync = new();
    private static readonly Dictionary<GameMode, string> LastMapByMode = new();
    private static List<MapPlaylistEntry<GameMode>> _mapPlaylist = new();
    private static System.Random? _mapPlaylistRandom;
    private static int _mapPlaylistIndex = -1;
    private static bool _mapPlaylistPrepared;
    private static float _nextClientLobbyPollTime;

    internal static void Initialize()
    {
        Plugin.DebugLogging = Plugin.Instance.Config.Bind("Global Settings", "Debug Logging", false,
            "Enable detailed multiplayer, scene, HUD, and snapshot diagnostics.");
        RespawnDelaySeconds = Plugin.Instance.Config.Bind("Global Settings", "Respawn Delay (seconds)", 3f,
            new ConfigDescription("Host-controlled: how long a killed player waits before respawning.",
                new AcceptableValueRange<float>(0f, 10f)));
        RespawnDelaySeconds.SettingChanged += (_, _) => OnGlobalSettingsChanged();
        PointsToWin = Plugin.Instance.Config.Bind("Global Settings", "Points To Win", ScoreRules.PointsToWin,
            "Fixed score limit for all point-based game modes.");
        PointsToWin.Value = ScoreRules.PointsToWin;
        PointsToWin.SettingChanged += (_, _) => OnGlobalSettingsChanged();

        MyceliumNetwork.RegisterNetworkObject(Plugin.Instance, ModId);
        ModeLobbyDataSync.RegisterKeys(ActiveModeLobbyDataKey);
        MyceliumNetwork.LobbyCreated += OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += OnPlayerEntered;
    }

    private static void OnGlobalSettingsChanged()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            ApplyGlobalSettingsFromHostConfig();
            BroadcastGlobalSettings();
        }
    }

    private static void ApplyGlobalSettingsFromHostConfig()
    {
        ApplyGlobalSettings(RespawnDelaySeconds.Value, PointsToWin.Value);
    }

    internal static void ApplyGlobalSettings(float respawnDelaySeconds, int pointsToWin)
    {
        EffectiveRespawnDelaySeconds = Mathf.Clamp(respawnDelaySeconds, 0f, 10f);
        int nextPointsToWin = pointsToWin;
        if (EffectivePointsToWin != nextPointsToWin)
        {
            EffectivePointsToWin = nextPointsToWin;
            ResetPointModeStates();
        }
    }

    private static void ResetPointModeStates()
    {
        DefaultGameModeState.ResetMatchState();
        FFAState.ResetMatchState();
        JuggernautState.ResetMatchState();
        GunGameState.ResetMatchState();
        SniperBattleState.ResetMatchState();
        HVTState.ResetMatchState();
        AssassinState.ResetMatchState();
        HardpointState.ResetMatchState();
    }

    private static void BroadcastGlobalSettings()
    {
        MyceliumNetwork.RPC(ModId, nameof(Plugin.SyncGlobalSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, RoundId, Sync.NextSettingsRevision(),
            EffectiveRespawnDelaySeconds, EffectivePointsToWin);
    }

    internal static void OnSettingsChanged()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost && !IsMatchOver)
        {
            bool restartRound = Phase == GameModePhase.ActiveRound
                && ActiveMode != GameMode.None && !IsEnabled(ActiveMode);
            EnsureActiveMode();
            if (restartRound)
            {
                RestartRoundAfterModeDisabled();
            }
        }
    }

    private static void RestartRoundAfterModeDisabled()
    {
        if (Plugin.Instance == null)
        {
            return;
        }

        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                GameModeRespawn.Schedule(client.PlayerId, EffectiveRespawnDelaySeconds);
            }
        }

        Plugin.Instance.StartCoroutine(BeginRoundAfterModeDisabled(
            EffectiveRespawnDelaySeconds + 0.75f, SessionState.Generation, RoundId, ActiveMode));
    }

    private static IEnumerator BeginRoundAfterModeDisabled(float delay, int sessionGeneration,
        int roundId, GameMode mode)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || RoundId != roundId
            || ActiveMode != mode || IsMatchOver || !MyceliumNetwork.IsHost)
        {
            yield break;
        }

        BeginRound();
        switch (ActiveMode)
        {
            case GameMode.MichaelMeyers:
                MichaelMeyersState.OnRoundStarted();
                break;
            case GameMode.OneInTheChamber:
                OneInTheChamberState.OnRoundStarted();
                break;
            case GameMode.HotPotato:
                HotPotatoState.OnRoundStarted();
                break;
            case GameMode.Infidel:
                InfidelState.OnRoundStarted();
                break;
            case GameMode.Assassin:
                AssassinState.OnRoundStarted();
                break;
            case GameMode.Hardpoint:
                HardpointState.OnRoundStarted();
                break;
        }
    }

    internal static void PeriodicPushIfHost()
    {
        if (!Sync.IsSettingsPushDue())
        {
            return;
        }
        ApplyGlobalSettingsFromHostConfig();
        BroadcastGlobalSettings();
        BroadcastActiveMode();
        if (Modes.TryGetValue(ActiveMode, out ModeDescriptor? activeDescriptor))
        {
            activeDescriptor.PeriodicSettingsPush();
        }
    }

    internal static void PeriodicActiveModePushIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        if (Modes.TryGetValue(ActiveMode, out ModeDescriptor? descriptor))
        {
            descriptor.PeriodicPush();
        }
    }

    internal static void PollLobbyStateIfClient()
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || Time.unscaledTime < _nextClientLobbyPollTime)
        {
            return;
        }

        _nextClientLobbyPollTime = Time.unscaledTime + 1f;
        ApplyLobbyActiveModeSnapshot();
    }

    internal static void EnsureActiveModeLoadouts()
    {
        if (Modes.TryGetValue(ActiveMode, out ModeDescriptor? descriptor))
        {
            descriptor.EnsureLoadouts();
        }
    }

    internal static bool CycleForNextMap()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return false;
        }
        _customRoundTransitionPending = false;

        if (!TrySelectNextPlaylistEntry(out MapPlaylistEntry<GameMode> entry))
        {
            GameMode nextMode = NextEnabledMode(ActiveMode);
            SetDefaultMapForMode(nextMode);
            ActivateMode(nextMode, true);
            return false;
        }

        ActivateMode(entry.Mode, true);
        return TryLoadSelectedMap();
    }

    internal static bool HandleSceneChange()
    {
        DebugLog.Info($"Scene change requested host={MyceliumNetwork.IsHost} scene={SceneManager.GetActiveScene().name} "
            + $"mode={ActiveMode} phase={Phase} round={RoundId} sceneIndex={SceneMotor.Instance?.sceneIndex ?? -1}");
        if (!MyceliumNetwork.IsHost)
        {
            return false;
        }

        if (IsFinalMatchTransition())
        {
            EndMatch();
            return false;
        }

        return CycleForNextMap();
    }

    internal static void StartMatch()
    {
        DebugLog.Info($"StartMatch called host={MyceliumNetwork.IsHost} lobby={MyceliumNetwork.InLobby} "
            + $"mode={ActiveMode} phase={Phase} round={RoundId} matchOver={IsMatchOver}");
        if (!MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || (ActiveMode != GameMode.None && Phase != GameModePhase.Lobby && !IsMatchOver))
        {
            return;
        }

        PrepareMapPlaylist();
        if (_mapPlaylistIndex < 0 && !TrySelectNextPlaylistEntry(out _))
        {
            GameMode fallbackMode = NextEnabledMode(GameMode.None);
            SetDefaultMapForMode(fallbackMode);
            ActivateMode(fallbackMode, true);
            return;
        }

        MapPlaylistEntry<GameMode> entry = _mapPlaylist[_mapPlaylistIndex];
        SelectedMapName = entry.MapName;
        ActivateMode(entry.Mode, true);
    }

    private static bool IsFinalMatchTransition()
    {
        if (SceneMotor.Instance == null || ScoreManager.Instance == null)
        {
            return false;
        }

        if (!SceneMotor.Instance.firstToXWins)
        {
            return SceneMotor.Instance.sceneIndex == 0;
        }

        if (SceneMotor.Instance.roundAmount <= 0)
        {
            return false;
        }

        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && ScoreManager.Instance.GetPoints(ScoreManager.Instance.GetTeamId(client.PlayerId))
                >= SceneMotor.Instance.roundAmount)
            {
                return true;
            }
        }

        return false;
    }

    private static void EndMatch()
    {
        DebugLog.Info($"EndMatch before reset mode={ActiveMode} phase={Phase} round={RoundId}");
        ResetMatchState();
        ActiveMode = GameMode.None;
        Phase = GameModePhase.Inactive;
        RoundId++;
        ResetMapPlaylist();
        if (MyceliumNetwork.InLobby)
        {
            BroadcastActiveMode();
        }
    }

    internal static bool IsActive(GameMode mode)
    {
        return ActiveMode == mode;
    }

    internal static bool ShouldIgnoreGlobalWeaponSettings =>
        HasCapability(GameModeCapabilities.IgnoreGlobalWeapons);

    internal static bool ShouldIgnoreGlobalHealthSettings => HasCapability(GameModeCapabilities.IgnoreGlobalHealth);

    internal static bool ShouldIgnoreGlobalMovementSettings =>
        HasCapability(GameModeCapabilities.IgnoreGlobalMovement);

    internal static bool IsCustomMode => HasCapability(GameModeCapabilities.CustomRound);
    internal static bool UsesSafeRespawn => HasCapability(GameModeCapabilities.SafeRespawn);
    internal static bool IsTeamBased => HasCapability(GameModeCapabilities.TeamBased);
    internal static bool ShouldHideCustomHud => HasCapability(GameModeCapabilities.HideHud);
    internal static bool ShouldClearPlayerOutlines => HasCapability(GameModeCapabilities.ClearOutlines);
    internal static bool IsMatchOver => (PauseManager.Instance != null && PauseManager.Instance.inVictoryMenu)
        || SceneManager.GetActiveScene().name == "VictoryScene"
        || SceneManager.GetActiveScene().name == "EndGame";

    internal static bool ShouldIgnoreGlobalWeaponSettingsFor(Weapon weapon)
    {
        return ShouldIgnoreGlobalWeaponSettings ||
            (ActiveMode == GameMode.Juggernaut && JuggernautState.IsCurrentJuggernautWeapon(weapon));
    }

    internal static string GetModeLabelMarkup(GameMode mode)
    {
        if (!Modes.TryGetValue(mode, out ModeDescriptor? descriptor))
        {
            return "<b>UNKNOWN</b>";
        }

        return $"<b><color=#{ColorUtility.ToHtmlStringRGB(descriptor.Color)}>{descriptor.Label}</color></b>";
    }

    internal static void EnsureActiveMode()
    {
        if (IsEnabled(ActiveMode))
        {
            return;
        }
        SetActiveMode(NextEnabledMode(ActiveMode));
    }

    private static void OnLobbyEntered()
    {
        DebugLog.Info($"Lobby entered/created host={MyceliumNetwork.IsHost} lobby={MyceliumNetwork.InLobby} "
            + $"lobbyHost={MyceliumNetwork.LobbyHost.m_SteamID} mode={ActiveMode} phase={Phase} round={RoundId}");
        Sync.ResetForLobby();
        SessionState.BeginLobby();
        _nextClientLobbyPollTime = 0f;
        if (MyceliumNetwork.IsHost)
        {
            ApplyGlobalSettingsFromHostConfig();
            ResetMapPlaylist();
            GameMode initialMode = NextEnabledMode(GameMode.None);
            SetDefaultMapForMode(initialMode);
            BroadcastGlobalSettings();
            ActivateMode(initialMode, true);
        }
        else
        {
            ApplyLobbyActiveModeSnapshot();
        }
    }

    private static void OnLobbyDataUpdated(List<string> keys)
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !ModeLobbyDataSync.ContainsKey(keys, ActiveModeLobbyDataKey))
        {
            return;
        }

        ApplyLobbyActiveModeSnapshot();
    }

    private static void OnLobbyLeft()
    {
        DebugLog.Info($"Lobby left mode={ActiveMode} phase={Phase} round={RoundId}");
        SessionState.EndLobby();
        ResetMatchState();
        ActiveMode = GameMode.None;
        Phase = GameModePhase.Inactive;
        RoundId++;
        ResetMapPlaylist();
        _nextClientLobbyPollTime = 0f;
        EffectiveRespawnDelaySeconds = 3f;
        EffectivePointsToWin = ScoreRules.PointsToWin;
        GlobalModifiersState.ResetForLobbyLeft();
        HealthSettingsState.ResetForLobbyLeft();
        WeaponSettingsState.ResetForLobbyLeft();
        WeaponService.ResetPendingRequests();
        GameModeRespawn.ResetForLobbyLeft();
        RespawnProtection.ResetState();
    }

    private static void OnPlayerEntered(CSteamID player)
    {
        DebugLog.Info($"Player entered player={player.m_SteamID} host={MyceliumNetwork.IsHost} "
            + $"localPlayer={ClientInstance.Instance?.PlayerId ?? -1} mode={ActiveMode} round={RoundId}");
        if (MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPCTarget(ModId, nameof(Plugin.SyncGlobalSettings), player,
                ReliableType.Reliable, MyceliumNetwork.LobbyHost, RoundId, Sync.SettingsRevision,
                EffectiveRespawnDelaySeconds, EffectivePointsToWin);
            MyceliumNetwork.RPCTarget(ModId, nameof(Plugin.SyncActiveGameMode), player,
                ReliableType.Reliable, MyceliumNetwork.LobbyHost, (int)ActiveMode, RoundId,
                (int)Phase, Sync.LiveRevision, SelectedMapName);
        }
    }

    private static GameMode NextEnabledMode(GameMode current)
    {
        List<GameMode> modes = GetConfiguredModes();
        return ModeCycle.TrySelectRandom(modes, current, UnityEngine.Random.Range(0, int.MaxValue), out GameMode next)
            ? next
            : GameMode.None;
    }

    internal static bool TryPrepareInitialMap(out string mapName)
    {
        mapName = string.Empty;
        if (!MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby)
        {
            return false;
        }

        PrepareMapPlaylist();
        if (_mapPlaylistIndex < 0 && !TrySelectNextPlaylistEntry(out _))
        {
            return false;
        }

        mapName = SelectedMapName;
        return !string.IsNullOrEmpty(mapName);
    }

    internal static bool TryGetCurrentMapDefinition(out MapDefinition definition)
    {
        if (ActiveMode != GameMode.None && !string.IsNullOrEmpty(SelectedMapName))
        {
            return ModeMapCatalog.TryGetDefinition(ActiveMode, SelectedMapName, out definition!);
        }

        definition = null!;
        return false;
    }

    private static void PrepareMapPlaylist()
    {
        if (_mapPlaylistPrepared)
        {
            return;
        }

        _mapPlaylistRandom = new System.Random(UnityEngine.Random.Range(0, int.MaxValue));
        _mapPlaylist = MapPlaylist.Build(GetConfiguredModes(), ModeMapCatalog.GetMapNames,
            _mapPlaylistRandom);
        _mapPlaylistIndex = -1;
        LastMapByMode.Clear();
        _mapPlaylistPrepared = true;
        DebugLog.Info($"[MapPlaylist] Prepared entries={_mapPlaylist.Count}");
        foreach (MapPlaylistEntry<GameMode> entry in _mapPlaylist)
        {
            DebugLog.Info($"[MapPlaylist] Candidate mode={entry.Mode} map={entry.MapName}");
        }
    }

    private static bool TrySelectNextPlaylistEntry(out MapPlaylistEntry<GameMode> entry)
    {
        PrepareMapPlaylist();
        if (_mapPlaylist.Count == 0 || _mapPlaylistRandom == null)
        {
            entry = default;
            return false;
        }

        int nextIndex = _mapPlaylistIndex;
        for (int offset = 0; offset < _mapPlaylist.Count; offset++)
        {
            nextIndex = (nextIndex + 1) % _mapPlaylist.Count;
            if (IsEnabled(_mapPlaylist[nextIndex].Mode))
            {
                break;
            }
        }

        if (!IsEnabled(_mapPlaylist[nextIndex].Mode))
        {
            entry = default;
            return false;
        }

        entry = _mapPlaylist[nextIndex];
        string mapName = entry.MapName;
        if (_mapPlaylistIndex >= 0 && LastMapByMode.TryGetValue(entry.Mode, out string? previousMap))
        {
            mapName = MapPlaylist.SelectNextMap(ModeMapCatalog.GetMapNames(entry.Mode), previousMap,
                _mapPlaylistRandom);
        }

        if (string.IsNullOrEmpty(mapName)
            || !ModeMapCatalog.IsSupported(entry.Mode, mapName))
        {
            entry = default;
            return false;
        }

        entry = new MapPlaylistEntry<GameMode>(entry.Mode, mapName);
        _mapPlaylist[nextIndex] = entry;
        _mapPlaylistIndex = nextIndex;
        LastMapByMode[entry.Mode] = mapName;
        SelectedMapName = mapName;
        DebugLog.Info($"[MapPlaylist] Selected index={_mapPlaylistIndex} mode={entry.Mode} map={mapName}");
        return true;
    }

    private static void SetDefaultMapForMode(GameMode mode)
    {
        SelectedMapName = string.Empty;
        foreach (string mapName in ModeMapCatalog.GetMapNames(mode))
        {
            if (ModeMapCatalog.IsSupported(mode, mapName))
            {
                SelectedMapName = mapName;
                break;
            }
        }
    }

    private static bool TryLoadSelectedMap()
    {
        if (!MyceliumNetwork.IsHost || SceneMotor.Instance == null
            || string.IsNullOrEmpty(SelectedMapName)
            || !ModeMapCatalog.IsSupported(ActiveMode, SelectedMapName)
            || InstanceFinder.SceneManager == null)
        {
            return false;
        }

        try
        {
            if (SceneManager.GetActiveScene().name == SelectedMapName)
            {
                FishNetSceneLoadData emptySceneLoadData = new("EmptyScene")
                {
                    ReplaceScenes = FishNetReplaceOption.All
                };
                InstanceFinder.SceneManager.LoadGlobalScenes(emptySceneLoadData);
            }

            FishNetSceneLoadData sceneLoadData = new(SelectedMapName)
            {
                ReplaceScenes = FishNetReplaceOption.All
            };
            InstanceFinder.SceneManager.LoadGlobalScenes(sceneLoadData);
            SceneMotor.Instance.IncrementScene();
            DebugLog.Info($"[Map] Loading mode={ActiveMode} map={SelectedMapName} round={RoundId}");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogError($"[Map] Failed to load map={SelectedMapName}: "
                + exception.GetBaseException().Message);
            return false;
        }
    }

    private static void ResetMapPlaylist()
    {
        _mapPlaylist.Clear();
        _mapPlaylistRandom = null;
        _mapPlaylistIndex = -1;
        _mapPlaylistPrepared = false;
        LastMapByMode.Clear();
        SelectedMapName = string.Empty;
    }

    private static List<GameMode> GetConfiguredModes()
    {
        List<GameMode> modes = new();
        foreach (GameMode mode in ModeOrder)
        {
            if (IsEnabled(mode) && !modes.Contains(mode))
            {
                modes.Add(mode);
            }
        }
        return modes;
    }

    private static bool IsEnabled(GameMode mode)
    {
        return Modes.TryGetValue(mode, out ModeDescriptor? descriptor)
            && descriptor.IsEnabled()
            && HarmonyPatchStatus.IsModeAvailable(mode);
    }

    private static void SetActiveMode(GameMode mode)
    {
        ActivateMode(mode, false);
    }

    private static void ActivateMode(GameMode mode, bool forceReset)
    {
        DebugLog.Info($"ActivateMode from={ActiveMode} to={mode} forceReset={forceReset} "
            + $"beforePhase={Phase} beforeRound={RoundId} host={MyceliumNetwork.IsHost}");
        if (!forceReset && ActiveMode == mode)
        {
            return;
        }

        ResetMatchState();
        ActiveMode = mode;
        Phase = MyceliumNetwork.InLobby ? GameModePhase.Lobby : GameModePhase.Inactive;
        RoundId++;
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            BroadcastActiveMode();
        }
    }

    internal static bool TryAcceptGlobalSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static bool TryAcceptActiveModeSnapshot(CSteamID hostId, int roundId, int revision,
        string source = "unknown")
    {
        return Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source);
    }

    internal static void ApplyActiveMode(int mode, int roundId, int phase, string mapName)
    {
        DebugLog.Info($"ApplyActiveMode input mode={(GameMode)mode} round={roundId} phase={(GameModePhase)phase} "
            + $"map={mapName} currentMode={ActiveMode} currentPhase={Phase} currentRound={RoundId}");
        if (!Enum.IsDefined(typeof(GameMode), mode) || !Enum.IsDefined(typeof(GameModePhase), phase)
            || roundId < RoundId)
        {
            return;
        }

        GameMode nextMode = (GameMode)mode;
        if (nextMode != GameMode.None
            && !ModeMapCatalog.IsSupported(nextMode, mapName))
        {
            DebugLog.Info($"[GameMode] Rejected active map mode={nextMode} map={mapName}");
            return;
        }

        if (!HarmonyPatchStatus.IsModeAvailable(nextMode))
        {
            ResetMatchState();
            ActiveMode = GameMode.None;
            Phase = GameModePhase.Inactive;
            RoundId = roundId;
            SelectedMapName = string.Empty;
            return;
        }

        bool newRound = roundId > RoundId;
        bool mapChanged = SelectedMapName != mapName;
        bool modeChanged = ActiveMode != nextMode || newRound || mapChanged;
        bool phaseChanged = Phase != (GameModePhase)phase;
        if (modeChanged)
        {
            ResetMatchState();
            ActiveMode = nextMode;
        }

        RoundId = roundId;
        Phase = (GameModePhase)phase;
        SelectedMapName = nextMode == GameMode.None ? string.Empty : mapName;
        if (modeChanged || phaseChanged)
        {
            DebugLog.Info($"[GameMode] Applied host state: mode={nextMode} round={roundId} phase={(GameModePhase)phase}");
        }
    }

    internal static void ResetMatchState()
    {
        _customRoundTransitionPending = false;
        PendingDeaths.Clear();
        GameModeRespawn.ResetForMatch();
        foreach (ModeDescriptor descriptor in Modes.Values)
        {
            descriptor.Reset();
        }
    }

    internal static void ResetGameState()
    {
        DebugLog.Info($"ResetGameState host={MyceliumNetwork.IsHost} mode={ActiveMode} phase={Phase} round={RoundId}");
        if (Phase == GameModePhase.EndingRound)
        {
            PendingDeaths.Clear();
            GameModeRespawn.ResetForMatch();
            return;
        }

        ResetMatchState();
        if (MyceliumNetwork.IsHost)
        {
            RoundId++;
            Phase = ActiveMode == GameMode.None || !MyceliumNetwork.InLobby
                ? GameModePhase.Inactive
                : GameModePhase.Lobby;
            return;
        }

        Phase = ActiveMode == GameMode.None ? GameModePhase.Inactive : GameModePhase.Lobby;
    }

    internal static void BeginRound()
    {
        DebugLog.Info($"BeginRound host={MyceliumNetwork.IsHost} mode={ActiveMode} phase={Phase} round={RoundId}");
        if (ActiveMode == GameMode.None)
        {
            return;
        }

        Phase = GameModePhase.ActiveRound;
        if (MyceliumNetwork.IsHost)
        {
            RoundId++;
            BroadcastActiveMode();
        }
    }

    private static bool HasCapability(GameModeCapabilities capability)
    {
        return Modes.TryGetValue(ActiveMode, out ModeDescriptor? descriptor)
            && descriptor.Capabilities.HasFlag(capability);
    }

    private static void BroadcastActiveMode()
    {
        int revision = Sync.NextLiveRevision();
        DebugLog.Info($"BroadcastActiveMode host={MyceliumNetwork.LobbyHost.m_SteamID} mode={ActiveMode} "
            + $"map={SelectedMapName} phase={Phase} round={RoundId} revision={revision} players={MyceliumNetwork.PlayerCount}");
        PublishActiveModeSnapshot(revision);
        MyceliumNetwork.RPC(ModId, nameof(Plugin.SyncActiveGameMode), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, (int)ActiveMode, RoundId, (int)Phase, revision,
            SelectedMapName);
    }

    private static void PublishActiveModeSnapshot(int revision)
    {
        string payload = string.Join("|", MyceliumNetwork.LobbyHost.m_SteamID,
            (int)ActiveMode, RoundId, (int)Phase, revision, SelectedMapName);
        ModeLobbyDataSync.PublishRaw(ActiveModeLobbyDataKey, payload);
    }

    private static void ApplyLobbyActiveModeSnapshot()
    {
        if (!ModeLobbyDataSync.TryReadOrdered(ActiveModeLobbyDataKey, 6, 0, 2, 4,
            out CSteamID hostId, out int roundId, out int revision, out string[] parts)
            || !int.TryParse(parts[1], out int mode)
            || !int.TryParse(parts[3], out int phase))
        {
            return;
        }

        if (!Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, "active-mode-lobby-data"))
        {
            return;
        }

        ApplyActiveMode(mode, roundId, phase, parts[5]);
        DebugLog.Info($"[GameMode] Accepted active mode via lobby data: mode={(GameMode)mode} "
            + $"map={parts[5]} round={roundId} phase={(GameModePhase)phase} revision={revision}");
    }

    private static readonly HashSet<int> PendingDeaths = new();
    private static bool _customRoundTransitionPending;

    internal static void CompleteCustomRound(int winningTeamId)
    {
        if (!MyceliumNetwork.IsHost || _customRoundTransitionPending || RoundManager.Instance == null
            || ScoreManager.Instance == null || SceneMotor.Instance == null || Plugin.Instance == null)
        {
            return;
        }

        _customRoundTransitionPending = true;
        Phase = GameModePhase.EndingRound;
        BroadcastActiveMode();
        int roundId = RoundId;

        ScoreManager.Instance.ResetRound();
        ScoreManager.Instance.AddPoints(winningTeamId);
        RoundManager.Instance.CmdEndRound(winningTeamId);
        Plugin.Instance.StartCoroutine(AdvanceAfterCustomRound(roundId));
    }

    private static IEnumerator AdvanceAfterCustomRound(int roundId)
    {
        int sessionGeneration = SessionState.Generation;
        yield return new WaitForSeconds(4f);
        if (SessionState.IsCurrent(sessionGeneration) && roundId == RoundId
            && Phase == GameModePhase.EndingRound && SceneMotor.Instance != null)
        {
            SceneMotor.Instance.ChangeNetworkScene();
        }
    }

    internal static bool HandleServerDeath(int playerId)
    {
        GameMode mode = ActiveMode;
        if (mode == GameMode.None || Plugin.Instance == null)
        {
            return false;
        }

        if (mode != GameMode.MichaelMeyers && mode != GameMode.OneInTheChamber
            && !FishNetCompatibility.CanRespawn)
        {
            Plugin.Logger.LogWarning($"[GameMode] Custom death handling disabled for {mode}: FishNet respawn API is unavailable.");
            return false;
        }

        if (PendingDeaths.Add(playerId))
        {
            Plugin.Instance.StartCoroutine(ProcessServerDeath(playerId, mode, RoundId, SessionState.Generation));
        }
        return true;
    }

    private static IEnumerator ProcessServerDeath(int playerId, GameMode mode, int roundId, int sessionGeneration)
    {
        // Gun's lethal-hit RPC calls PlayerDied before it writes PlayerHealth.killer.
        // Let that RPC finish before resolving the attacker.
        yield return null;
        PendingDeaths.Remove(playerId);

        if (!SessionState.IsCurrent(sessionGeneration) || ActiveMode != mode || RoundId != roundId
            || Phase == GameModePhase.EndingRound)
        {
            yield break;
        }

        PlayerHealth? deadHealth = PlayerLookup.FindPlayerHealthById(playerId);
        int killerId = PlayerLookup.FindKillerId(deadHealth);
        DebugLog.Info($"[GameMode] Server death: mode={mode} deadPlayer={playerId} killer={killerId}");

        switch (mode)
        {
            case GameMode.FreeForAll:
                FFAState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.Default:
                DefaultGameModeState.OnServerKill(playerId);
                break;
            case GameMode.Juggernaut:
                JuggernautState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.GunGame:
                GunGameState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.SniperBattle:
                SniperBattleState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.MichaelMeyers:
                MichaelMeyersState.OnServerKill(playerId, killerId);
                break;
            case GameMode.KillTheRat:
                KillTheRatState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.OneInTheChamber:
                OneInTheChamberState.OnServerKill(playerId, killerId);
                break;
            case GameMode.HotPotato:
                HotPotatoState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.Infidel:
                InfidelState.OnServerKill(playerId, killerId);
                break;
            case GameMode.Assassin:
                AssassinState.OnServerKill(playerId, killerId);
                break;
            case GameMode.Hardpoint:
                HardpointState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.HVT:
                HVTState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
        }
    }
}

[HarmonyLib.HarmonyPatch]
internal static class GameManager_GameModeDeath_Patch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return FishNetCompatibility.FindGeneratedMethod(typeof(GameManager), "RpcLogic___PlayerDied_",
            method => method.ReturnType == typeof(void)
                && method.GetParameters() is { Length: 1 } parameters
                && parameters[0].ParameterType == typeof(int));
    }

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(GameManager __instance, int playerId)
    {
        if (!__instance.IsServer || !GameModeManager.IsCustomMode)
        {
            return true;
        }

        return !GameModeManager.HandleServerDeath(playerId);
    }
}

public partial class Plugin
{
    [CustomRPC]
    public void SyncScorePopup(int amount, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || amount <= 0 || amount > ScoreRules.PointsToWin)
        {
            return;
        }

        GameModeHud.ShowScorePopup(amount);
    }

    [CustomRPC]
    public void SyncGlobalSettings(CSteamID hostId, int roundId, int revision, float respawnDelaySeconds,
        int pointsToWin, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!GameModeManager.TryAcceptGlobalSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        GameModeManager.ApplyGlobalSettings(respawnDelaySeconds, pointsToWin);
    }

    [CustomRPC]
    public void SyncActiveGameMode(CSteamID hostId, int mode, int roundId, int phase, int revision,
        string mapName, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!GameModeManager.TryAcceptActiveModeSnapshot(hostId, roundId, revision, "active-mode-rpc"))
        {
            return;
        }
        GameModeManager.ApplyActiveMode(mode, roundId, phase, mapName);
        DebugLog.Info($"[GameMode] Accepted active mode via RPC: mode={(GameMode)mode} "
            + $"map={mapName} round={roundId} phase={(GameModePhase)phase} revision={revision}");
    }
}

[HarmonyLib.HarmonyPatch(typeof(GameManager), "ResetGame")]
internal static class GameManager_GameModeReset_Patch
{
    private static void Postfix()
    {
        DebugLog.Info($"GameManager.ResetGame postfix host={MyceliumNetwork.IsHost} "
            + $"mode={GameModeManager.ActiveMode} phase={GameModeManager.Phase} round={GameModeManager.RoundId}");
        GameModeManager.ResetGameState();
        PlayerOutline.ResetState();
        RespawnProtection.ResetState();
    }
}

[HarmonyLib.HarmonyPatch(typeof(SceneMotor), "ChangeNetworkScene")]
internal static class SceneMotor_GameModeCycle_Patch
{
    private static bool Prefix()
    {
        DebugLog.Info($"SceneMotor.ChangeNetworkScene prefix scene={SceneManager.GetActiveScene().name} "
            + $"host={MyceliumNetwork.IsHost} mode={GameModeManager.ActiveMode} "
            + $"phase={GameModeManager.Phase} round={GameModeManager.RoundId}");
        return !GameModeManager.HandleSceneChange();
    }
}

[HarmonyLib.HarmonyPatch(typeof(SceneMotor), "GetNextMap")]
internal static class SceneMotor_GameModeInitialMap_Patch
{
    private static bool Prefix(ref string __result)
    {
        if (!GameModeManager.TryPrepareInitialMap(out string mapName))
        {
            return true;
        }

        __result = mapName;
        return false;
    }
}

[HarmonyLib.HarmonyPatch(typeof(SceneMotor), "ServerStartGameScene")]
internal static class SceneMotor_GameModeStart_Patch
{
    private static void Prefix(SceneMotor __instance)
    {
        if (GameModeManager.TryPrepareInitialMap(out string mapName)
            && __instance.PlayListMaps.Count == 0)
        {
            __instance.PlayListMaps.Add(mapName);
        }
    }
}

[HarmonyLib.HarmonyPatch(typeof(GameManager), "StartGame")]
internal static class GameManager_GameModeStart_Patch
{
    private static void Postfix()
    {
        DebugLog.Info($"GameManager.StartGame postfix host={MyceliumNetwork.IsHost} "
            + $"scene={SceneManager.GetActiveScene().name} mode={GameModeManager.ActiveMode} "
            + $"phase={GameModeManager.Phase} round={GameModeManager.RoundId}");
        GameModeManager.StartMatch();
    }
}