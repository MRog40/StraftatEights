using System.Collections;
using MyceliumNetworking;
using Steamworks;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
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
    Hardpoint = 13,
    CaptureTheFlag = 14,
    SearchAndDestroy = 15,
    TeamDeathmatch = 16
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
        internal readonly Action PollLiveState;

        internal ModeDescriptor(string label, Color color, Func<bool> isEnabled, Action reset,
            GameModeCapabilities capabilities, Action? periodicPush = null, Action? ensureLoadouts = null,
            Action? periodicSettingsPush = null, Action? pollLiveState = null)
        {
            Label = label;
            Color = color;
            IsEnabled = isEnabled;
            Reset = reset;
            Capabilities = capabilities;
            PeriodicSettingsPush = periodicSettingsPush ?? periodicPush ?? Noop;
            PeriodicPush = periodicPush ?? Noop;
            EnsureLoadouts = ensureLoadouts ?? Noop;
            PollLiveState = pollLiveState ?? Noop;
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
        GameMode.Hardpoint,
        GameMode.CaptureTheFlag,
        GameMode.SearchAndDestroy,
        GameMode.TeamDeathmatch
    };

    private static readonly Dictionary<GameMode, string> ScoreboardColors = new()
    {
        [GameMode.Default] = "F2F2F2",
        [GameMode.FreeForAll] = "7B61FF",
        [GameMode.Juggernaut] = "FF6B6B",
        [GameMode.GunGame] = "F4D35E",
        [GameMode.SniperBattle] = "E56BCA",
        [GameMode.MichaelMeyers] = "C44536",
        [GameMode.KillTheRat] = "A7C7E7",
        [GameMode.OneInTheChamber] = "D8B4FE",
        [GameMode.HotPotato] = "F28F3B",
        [GameMode.Infidel] = "B56576",
        [GameMode.HVT] = "6D597A",
        [GameMode.Assassin] = "C77DFF",
        [GameMode.Hardpoint] = "43D17A",
        [GameMode.CaptureTheFlag] = "FFCA3A",
        [GameMode.SearchAndDestroy] = "D1495B",
        [GameMode.TeamDeathmatch] = "00B4D8"
    };

    private static readonly Dictionary<GameMode, ModeDescriptor> Modes = new()
    {
        [GameMode.Default] = new ModeDescriptor("DEFAULT", new Color32(220, 220, 220, 255),
            () => Plugin.DefaultGameModeEnabled.Value, DefaultReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.IgnoreGlobalMovement
            | GameModeCapabilities.SafeRespawn,
            DefaultGameModeState.PeriodicPushIfHost,
            pollLiveState: DefaultGameModeState.PollLiveStateIfClient),
        [GameMode.FreeForAll] = new ModeDescriptor("FFA", new Color32(85, 204, 255, 255),
            () => Plugin.FFAEnabled.Value, FfaReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
                FFAState.PeriodicPushIfHost, TeamWeaponLoadouts.EnsureLoadouts,
                periodicSettingsPush: FFAState.PeriodicPushSettingsIfHost,
                    pollLiveState: FFAState.PollLiveStateIfClient),
        [GameMode.Juggernaut] = new ModeDescriptor("JUGGERNAUT", new Color32(255, 106, 0, 255),
            () => Plugin.JuggernautEnabled.Value, JuggernautReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.SafeRespawn,
            JuggernautState.PeriodicPushIfHost, JuggernautState.EnsureLoadout,
            JuggernautState.PeriodicPushSettingsIfHost,
            JuggernautState.PollLiveStateIfClient),
        [GameMode.GunGame] = new ModeDescriptor("GUN GAME", new Color32(255, 221, 85, 255),
            () => Plugin.GunGameEnabled.Value, GunGameReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
               GunGameState.PeriodicPushIfHost, GunGameState.EnsureLoadouts,
                    periodicSettingsPush: GunGameState.PeriodicPushSettingsIfHost,
                    pollLiveState: GunGameState.PollLiveStateIfClient),
        [GameMode.SniperBattle] = new ModeDescriptor("SNIPER BATTLE", new Color32(255, 96, 128, 255),
            () => Plugin.SniperBattleEnabled.Value, SniperBattleReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.ClearOutlines
            | GameModeCapabilities.SafeRespawn,
               SniperBattleState.PeriodicPushIfHost, SniperBattleState.EnsureLoadouts,
                    SniperBattleState.PeriodicPushSettingsIfHost,
                    SniperBattleState.PollLiveStateIfClient),
        [GameMode.MichaelMeyers] = new ModeDescriptor("MICHAEL MEYERS", new Color32(204, 34, 34, 255),
            () => Plugin.MichaelMeyersEnabled.Value, MichaelMeyersReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.HideHud,
            MichaelMeyersPeriodicPush, MichaelMeyersState.EnsureLoadouts,
            MichaelMeyersState.PeriodicPushSettingsIfHost,
            MichaelMeyersState.PollLiveStateIfClient),
        [GameMode.KillTheRat] = new ModeDescriptor("KILL THE RAT", new Color32(170, 170, 170, 255),
            () => Plugin.KillTheRatEnabled.Value, KillTheRatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
            KillTheRatState.PeriodicPushIfHost, KillTheRatState.EnsureLoadouts,
            KillTheRatState.PeriodicPushSettingsIfHost,
            KillTheRatState.PollLiveStateIfClient),
        [GameMode.OneInTheChamber] = new ModeDescriptor("ONE IN THE CHAMBER", new Color32(180, 180, 180, 255),
            () => Plugin.OneInTheChamberEnabled.Value, OneInTheChamberReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.SafeRespawn,
            OneInTheChamberState.PeriodicPushIfHost, OneInTheChamberState.EnsureLoadouts,
            OneInTheChamberState.PeriodicPushSettingsIfHost,
            OneInTheChamberState.PollLiveStateIfClient),
        [GameMode.HotPotato] = new ModeDescriptor("HOT POTATO", new Color32(255, 170, 70, 255),
            () => Plugin.HotPotatoEnabled.Value, HotPotatoReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
            HotPotatoState.PeriodicPushIfHost, HotPotatoState.EnsureLoadouts,
            HotPotatoState.PeriodicPushSettingsIfHost,
            HotPotatoState.PollLiveStateIfClient),
        [GameMode.Infidel] = new ModeDescriptor("INFIDEL", new Color32(204, 64, 64, 255),
            () => Plugin.InfidelEnabled.Value, InfidelReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.SafeRespawn,
            InfidelState.PeriodicPushIfHost, InfidelState.EnsureLoadouts,
            InfidelState.PeriodicPushSettingsIfHost,
            InfidelState.PollLiveStateIfClient),
        [GameMode.HVT] = new ModeDescriptor("HVT", new Color32(0, 0, 255, 255),
            () => Plugin.HVTEnabled.Value, HVTReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.SafeRespawn,
            HVTState.PeriodicPushIfHost, periodicSettingsPush: HVTState.PeriodicPushSettingsIfHost,
            pollLiveState: HVTState.PollLiveStateIfClient),
        [GameMode.Assassin] = new ModeDescriptor("ASSASSIN", new Color32(53, 208, 95, 255),
            () => Plugin.AssassinEnabled.Value, AssassinReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
            AssassinState.PeriodicPushIfHost, AssassinState.EnsureLoadouts,
            AssassinState.PeriodicPushSettingsIfHost,
            AssassinState.PollLiveStateIfClient),
        [GameMode.Hardpoint] = new ModeDescriptor("HARDPOINT", new Color32(0, 114, 178, 255),
            () => Plugin.HardpointEnabled.Value, HardpointReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            HardpointState.PeriodicPushIfHost, TeamWeaponLoadouts.EnsureLoadouts,
            HardpointState.PeriodicPushSettingsIfHost, HardpointState.PollLiveStateIfClient),
        [GameMode.CaptureTheFlag] = new ModeDescriptor("CAPTURE THE FLAG", new Color32(255, 190, 55, 255),
            () => Plugin.CaptureTheFlagEnabled.Value, CaptureTheFlagReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            CaptureTheFlagState.PeriodicPushIfHost, ensureLoadouts: TeamWeaponLoadouts.EnsureLoadouts,
            periodicSettingsPush: CaptureTheFlagState.PeriodicPushSettingsIfHost,
            pollLiveState: CaptureTheFlagState.PollLiveStateIfClient),
        [GameMode.SearchAndDestroy] = new ModeDescriptor("SEARCH AND DESTROY", new Color32(225, 70, 70, 255),
            () => Plugin.SearchAndDestroyEnabled.Value, SearchAndDestroyReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            SearchAndDestroyState.PeriodicPushIfHost, ensureLoadouts: TeamWeaponLoadouts.EnsureLoadouts,
            periodicSettingsPush: SearchAndDestroyState.PeriodicPushSettingsIfHost,
            pollLiveState: SearchAndDestroyState.PollLiveStateIfClient),
        [GameMode.TeamDeathmatch] = new ModeDescriptor("TDM", new Color32(255, 190, 55, 255),
            () => Plugin.TeamDeathmatchEnabled.Value, TeamDeathmatchReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            TeamDeathmatchState.PeriodicPushIfHost, ensureLoadouts: TeamWeaponLoadouts.EnsureLoadouts,
            periodicSettingsPush:
            TeamDeathmatchState.PeriodicPushSettingsIfHost,
            pollLiveState: TeamDeathmatchState.PollLiveStateIfClient)
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
    private static void CaptureTheFlagReset() => CaptureTheFlagState.ResetMatchState();
    private static void SearchAndDestroyReset() => SearchAndDestroyState.ResetMatchState();
    private static void TeamDeathmatchReset() => TeamDeathmatchState.ResetMatchState();

    internal static GameMode ActiveMode { get; private set; }
    internal static GameModePhase Phase { get; private set; } = GameModePhase.Inactive;
    internal static int RoundId { get; private set; }
    private static bool _roundLifecycleStarted;
    internal static string SelectedMapName { get; private set; } = string.Empty;
    internal static ConfigEntry<float> RespawnDelaySeconds = null!;
    internal static ConfigEntry<int> PointsToWin = null!;
    internal static ConfigEntry<bool> EnableMapOverrides = null!;
    internal static float EffectiveRespawnDelaySeconds { get; set; } = 2.5f;
    internal static int EffectivePointsToWin { get; private set; } = ScoreRules.PointsToWin;
    internal static bool EffectiveMapOverrides { get; private set; }
    private static readonly ModeSyncState Sync = new();
    private static readonly Dictionary<GameMode, string> LastMapByMode = new();
    private static List<MapPlaylistEntry<GameMode>> _mapPlaylist = new();
    private static System.Random? _mapPlaylistRandom;
    private static int _mapPlaylistIndex = -1;
    private static bool _mapPlaylistPrepared;
    private static float _nextClientLobbyPollTime;
    private static string _pendingNormalMapName = string.Empty;

    internal static void Initialize()
    {
        Plugin.DebugLogging = Plugin.Instance.Config.Bind("Global Settings", "Debug Logging", false,
            "Enable detailed multiplayer, scene, HUD, and snapshot diagnostics.");
        EnableMapOverrides = Plugin.Instance.Config.Bind("Global Settings", "Enable Map Overrides", false,
            "Host-controlled: use the plugin's mode-specific map overrides instead of the normal lobby map playlist.");
        EnableMapOverrides.SettingChanged += (_, _) => OnGlobalSettingsChanged();
        RespawnDelaySeconds = Plugin.Instance.Config.Bind("Global Settings", "Respawn Delay (seconds)", 2.5f,
            new ConfigDescription("Host-controlled: how long a killed player waits before respawning.",
                new AcceptableValueRange<float>(0f, 10f)));
        RespawnDelaySeconds.SettingChanged += (_, _) => OnGlobalSettingsChanged();
        PointsToWin = Plugin.Instance.Config.Bind("Global Settings", "Points To Win", ScoreRules.PointsToWin,
            "Fixed score limit for all point-based game modes.");
        PointsToWin.Value = ScoreRules.PointsToWin;
        PointsToWin.SettingChanged += (_, _) => OnGlobalSettingsChanged();

        MyceliumNetwork.RegisterNetworkObject(Plugin.Instance, ModId);
        ModeLobbyDataSync.RegisterKeys(ActiveModeLobbyDataKey, ModeTimeoutState.LiveLobbyDataKey);
        PlayerNameSync.Initialize();
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
            bool previousMapOverrides = EffectiveMapOverrides;
            ApplyGlobalSettingsFromHostConfig();
            if (previousMapOverrides != EffectiveMapOverrides)
            {
                string currentMapName = SelectedMapName;
                bool preserveCurrentMap = Phase == GameModePhase.ActiveRound
                    || Phase == GameModePhase.EndingRound;
                ResetMapPlaylist();
                if (preserveCurrentMap)
                {
                    SelectedMapName = currentMapName;
                }
                if (!EffectiveMapOverrides && Phase == GameModePhase.Lobby)
                {
                    ActivateMode(GameMode.None, true);
                }
            }
            BroadcastGlobalSettings();
        }
    }

    private static void ApplyGlobalSettingsFromHostConfig()
    {
        ApplyGlobalSettings(RespawnDelaySeconds.Value, PointsToWin.Value, EnableMapOverrides.Value);
    }

    internal static void ApplyGlobalSettings(float respawnDelaySeconds, int pointsToWin,
        bool enableMapOverrides)
    {
        EffectiveRespawnDelaySeconds = Mathf.Clamp(respawnDelaySeconds, 0f, 10f);
        EffectiveMapOverrides = enableMapOverrides;
        int nextPointsToWin = pointsToWin;
        if (EffectivePointsToWin != nextPointsToWin)
        {
            EffectivePointsToWin = nextPointsToWin;
            ResetPointModeStates();
        }
    }

    private static void ResetPointModeStates()
    {
        ModeTimeoutState.ResetMatchState();
        DefaultGameModeState.ResetMatchState();
        FFAState.ResetMatchState();
        JuggernautState.ResetMatchState();
        GunGameState.ResetMatchState();
        SniperBattleState.ResetMatchState();
        HVTState.ResetMatchState();
        AssassinState.ResetMatchState();
        HardpointState.ResetMatchState();
        CaptureTheFlagState.ResetMatchState();
        SearchAndDestroyState.ResetMatchState();
    }

    private static void BroadcastGlobalSettings()
    {
        MyceliumNetwork.RPC(ModId, nameof(Plugin.SyncGlobalSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, RoundId, Sync.NextSettingsRevision(),
            EffectiveRespawnDelaySeconds, EffectivePointsToWin, EffectiveMapOverrides);
    }

    internal static void OnSettingsChanged()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost && !IsMatchOver)
        {
            bool restartRound = Phase == GameModePhase.ActiveRound
                && ActiveMode != GameMode.None && !IsEnabled(ActiveMode);
            EnsureActiveMode();
            AddNewConfiguredModesToPlaylist();
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
                GameModeRespawn.Schedule(client.PlayerId, EffectiveRespawnDelaySeconds,
                    protectOnRespawn: false);
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
            || ActiveMode != mode || IsMatchOver || IsVanillaScene || !MyceliumNetwork.IsHost)
        {
            yield break;
        }

        if (!BeginRound())
        {
            yield break;
        }
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
            case GameMode.CaptureTheFlag:
                CaptureTheFlagState.OnRoundStarted();
                break;
            case GameMode.SearchAndDestroy:
                SearchAndDestroyState.OnRoundStarted();
                break;
            case GameMode.TeamDeathmatch:
                TeamDeathmatchState.OnRoundStarted();
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

        ModeTimeoutState.PeriodicPushIfHost();
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
        ModeTimeoutState.PollLiveStateIfClient();
        if (Modes.TryGetValue(ActiveMode, out ModeDescriptor? descriptor))
        {
            descriptor.PollLiveState();
        }
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
        if (IsVanillaScene || !MyceliumNetwork.IsHost)
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
        if (IsVanillaScene)
        {
            EnsureVanillaScene();
            return false;
        }

        if (!MyceliumNetwork.IsHost)
        {
            return false;
        }

        if (_skipRoundTransitionPending)
        {
            _skipRoundTransitionPending = false;
            return CycleForNextMap();
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
        if (IsVanillaScene)
        {
            EnsureVanillaScene();
            return;
        }

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
        if (!IsEnabled(entry.Mode)
            || !ModeMapCatalog.IsSupported(entry.Mode, entry.MapName, EffectiveMapOverrides))
        {
            DebugLog.Info($"StartMatch ignored stale playlist entry mode={entry.Mode} "
                + $"enabled={IsEnabled(entry.Mode)} map={entry.MapName}");
            if (!TrySelectNextPlaylistEntry(out entry))
            {
                GameMode fallbackMode = NextEnabledMode(GameMode.None);
                SetDefaultMapForMode(fallbackMode);
                ActivateMode(fallbackMode, true);
                return;
            }
        }

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
        return !IsVanillaScene && ActiveMode == mode;
    }

    internal static bool ShouldIgnoreGlobalWeaponSettings =>
        IsVanillaScene || HasCapability(GameModeCapabilities.IgnoreGlobalWeapons);

    internal static bool ShouldIgnoreGlobalHealthSettings =>
        IsVanillaScene || HasCapability(GameModeCapabilities.IgnoreGlobalHealth);

    internal static bool ShouldIgnoreGlobalMovementSettings =>
        IsVanillaScene || HasCapability(GameModeCapabilities.IgnoreGlobalMovement);

    internal static bool IsCustomMode => !IsVanillaScene && HasCapability(GameModeCapabilities.CustomRound);
    internal static bool UsesSafeRespawn => !IsVanillaScene && HasCapability(GameModeCapabilities.SafeRespawn);
    internal static bool IsTeamBased => !IsVanillaScene && HasCapability(GameModeCapabilities.TeamBased);
    internal static bool UsesTeamWeaponLoadouts => IsTeamBased || IsActive(GameMode.FreeForAll);
    internal static bool ShouldHideCustomHud => !IsVanillaScene && HasCapability(GameModeCapabilities.HideHud);
    internal static bool ShouldClearPlayerOutlines => !IsVanillaScene && HasCapability(GameModeCapabilities.ClearOutlines);
    internal static bool IsVanillaScene => SceneMotor.Instance != null && SceneMotor.Instance.testMap
        || SceneManager.GetActiveScene().name == "TrainingRange_00"
        || SceneManager.GetActiveScene().name == "TutorialScene";
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
        return GetModeLabelMarkup(mode, null);
    }

    internal static string GetModeLabelMarkup(GameMode mode, string? labelOverride)
    {
        if (!Modes.TryGetValue(mode, out ModeDescriptor? descriptor))
        {
            return "<b>Unknown</b>";
        }

        string label = ToReadableModeLabel(labelOverride ?? descriptor.Label);
        return $"<b><color=#{ColorUtility.ToHtmlStringRGB(descriptor.Color)}>{label}</color></b>";
    }

    private static string ToReadableModeLabel(string label)
    {
        return label switch
        {
            "FFA" => "FFA",
            "HVT" => "HVT",
            "TDM" => "TDM",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(label.ToLowerInvariant())
        };
    }

    internal static string GetScoreboardModeLabelMarkup(GameMode mode, string? labelOverride)
    {
        if (!Modes.TryGetValue(mode, out ModeDescriptor? descriptor))
        {
            return "<b>UNKNOWN</b>";
        }

        string label = labelOverride ?? descriptor.Label;
        string color = ScoreboardColors.TryGetValue(mode, out string? scoreboardColor)
            ? scoreboardColor
            : "F2F2F2";
        return $"<b><color=#{color}>{label}</color></b>";
    }

    internal static void EnsureActiveMode()
    {
        if (!EffectiveMapOverrides)
        {
            if (string.IsNullOrEmpty(SelectedMapName))
            {
                if (ActiveMode != GameMode.None)
                {
                    ActivateMode(GameMode.None, true);
                }
                return;
            }

            if (IsEnabled(ActiveMode)
                && ModeMapCatalog.IsSupported(ActiveMode, SelectedMapName, false))
            {
                return;
            }

            SelectModeForNormalMap(SelectedMapName);
            return;
        }

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
        ModeTimeoutState.OnLobbyEntered();
        _nextClientLobbyPollTime = 0f;
        if (MyceliumNetwork.IsHost)
        {
            ApplyGlobalSettingsFromHostConfig();
            ResetMapPlaylist();
            BroadcastGlobalSettings();
            if (EffectiveMapOverrides)
            {
                GameMode initialMode = NextEnabledMode(GameMode.None);
                SetDefaultMapForMode(initialMode);
                ActivateMode(initialMode, true);
            }
            else
            {
                ActivateMode(GameMode.None, true);
            }
        }
        else
        {
            ApplyLobbyActiveModeSnapshot();
        }
    }

    private static void OnLobbyDataUpdated(List<string> keys)
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || (!ModeLobbyDataSync.ContainsKey(keys, ActiveModeLobbyDataKey)
                && !ModeLobbyDataSync.ContainsKey(keys, ModeTimeoutState.LiveLobbyDataKey)))
        {
            return;
        }

        if (ModeLobbyDataSync.ContainsKey(keys, ActiveModeLobbyDataKey))
        {
            ApplyLobbyActiveModeSnapshot();
        }
        ModeTimeoutState.OnLobbyDataUpdated(keys);
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
        EffectiveRespawnDelaySeconds = 2.5f;
        EffectivePointsToWin = ScoreRules.PointsToWin;
        EffectiveMapOverrides = true;
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
                EffectiveRespawnDelaySeconds, EffectivePointsToWin, EffectiveMapOverrides);
            MyceliumNetwork.RPCTarget(ModId, nameof(Plugin.SyncActiveGameMode), player,
                ReliableType.Reliable, MyceliumNetwork.LobbyHost, (int)ActiveMode, RoundId,
                (int)Phase, Sync.LiveRevision, SelectedMapName, EffectiveMapOverrides);
            ModeTimeoutState.OnPlayerEntered(player);
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
        if (IsVanillaScene || !MyceliumNetwork.IsHost
            || !MyceliumNetwork.InLobby)
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
            return ModeMapCatalog.TryGetDefinition(ActiveMode, SelectedMapName,
                EffectiveMapOverrides, out definition!);
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
        _mapPlaylist = MapPlaylist.Build(GetConfiguredModes(),
            mode => ModeMapCatalog.GetMapNames(mode, EffectiveMapOverrides),
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

    private static void AddNewConfiguredModesToPlaylist()
    {
        if (!_mapPlaylistPrepared || _mapPlaylistRandom == null)
        {
            return;
        }

        HashSet<GameMode> playlistModes = new();
        foreach (MapPlaylistEntry<GameMode> entry in _mapPlaylist)
        {
            playlistModes.Add(entry.Mode);
        }

        foreach (GameMode mode in GetConfiguredModes())
        {
            if (!playlistModes.Add(mode))
            {
                continue;
            }

            string mapName = MapPlaylist.SelectNextMap(
                ModeMapCatalog.GetMapNames(mode, EffectiveMapOverrides), string.Empty,
                _mapPlaylistRandom);
            if (string.IsNullOrEmpty(mapName)
                || !ModeMapCatalog.IsSupported(mode, mapName, EffectiveMapOverrides))
            {
                continue;
            }

            int insertionStart = _mapPlaylistIndex < 0 ? 0 : _mapPlaylistIndex + 1;
            int insertionCount = _mapPlaylist.Count - insertionStart + 1;
            int insertionWindow = Math.Min(3, insertionCount);
            int insertionIndex = insertionStart + _mapPlaylistRandom.Next(insertionWindow);
            _mapPlaylist.Insert(insertionIndex, new MapPlaylistEntry<GameMode>(mode, mapName));
            DebugLog.Info($"[MapPlaylist] Added mode={mode} map={mapName} to active playlist "
                + $"at index={insertionIndex}");
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
            mapName = MapPlaylist.SelectNextMap(
                ModeMapCatalog.GetMapNames(entry.Mode, EffectiveMapOverrides), previousMap,
                _mapPlaylistRandom);
        }

        if (string.IsNullOrEmpty(mapName)
            || !ModeMapCatalog.IsSupported(entry.Mode, mapName, EffectiveMapOverrides))
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
        foreach (string mapName in ModeMapCatalog.GetMapNames(mode, EffectiveMapOverrides))
        {
            if (ModeMapCatalog.IsSupported(mode, mapName, EffectiveMapOverrides))
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
            || !ModeMapCatalog.IsSupported(ActiveMode, SelectedMapName, EffectiveMapOverrides)
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
        _pendingNormalMapName = string.Empty;
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

    private static GameMode NextEnabledModeForMap(GameMode current, string mapName)
    {
        List<GameMode> compatibleModes = new();
        foreach (GameMode mode in GetConfiguredModes())
        {
            if (ModeMapCatalog.IsSupported(mode, mapName, EffectiveMapOverrides))
            {
                compatibleModes.Add(mode);
            }
        }

        return ModeCycle.TrySelectRandom(compatibleModes, current,
            UnityEngine.Random.Range(0, int.MaxValue), out GameMode next)
            ? next
            : GameMode.None;
    }

    private static void SelectModeForNormalMap(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            ActivateMode(GameMode.None, true);
            return;
        }

        SelectedMapName = mapName;
        GameMode nextMode = NextEnabledModeForMap(ActiveMode, mapName);
        ActivateMode(nextMode, true);
    }

    internal static void OnNormalMapSelected(string mapName)
    {
        if (EffectiveMapOverrides || !MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby)
        {
            return;
        }

        _pendingNormalMapName = mapName ?? string.Empty;
        if (Phase == GameModePhase.EndingRound || ActiveMode != GameMode.None)
        {
            SelectModeForNormalMap(_pendingNormalMapName);
            _pendingNormalMapName = string.Empty;
        }
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
        if (MyceliumNetwork.IsHost && mode != GameMode.None && !IsEnabled(mode))
        {
            DebugLog.Info($"ActivateMode rejected disabled mode={mode}");
            mode = NextEnabledMode(mode);
            if (mode == GameMode.None)
            {
                SelectedMapName = string.Empty;
            }
        }

        if (!forceReset && ActiveMode == mode)
        {
            return;
        }

        ResetMatchState();
        ActiveMode = mode;
        Phase = MyceliumNetwork.InLobby ? GameModePhase.Lobby : GameModePhase.Inactive;
        RoundId++;
        if ((mode == GameMode.Hardpoint || mode == GameMode.CaptureTheFlag
            || mode == GameMode.SearchAndDestroy || mode == GameMode.TeamDeathmatch)
            && MyceliumNetwork.IsHost)
        {
            if (mode == GameMode.Hardpoint)
            {
                TeamAssignment.AssignForRound();
            }
            else if (mode == GameMode.CaptureTheFlag)
            {
                CaptureTheFlagState.PrepareTeamsForRound();
            }
            else if (mode == GameMode.SearchAndDestroy)
            {
                SearchAndDestroyState.PrepareTeamsForRound();
            }
            else
            {
                TeamDeathmatchState.PrepareTeamsForRound();
            }
        }
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

    internal static void ApplyActiveMode(int mode, int roundId, int phase, string mapName,
        bool mapOverridesEnabled)
    {
        DebugLog.Info($"ApplyActiveMode input mode={(GameMode)mode} round={roundId} phase={(GameModePhase)phase} "
            + $"map={mapName} currentMode={ActiveMode} currentPhase={Phase} currentRound={RoundId}");
        if (IsVanillaScene)
        {
            EnsureVanillaScene();
            return;
        }

        if (!Enum.IsDefined(typeof(GameMode), mode) || !Enum.IsDefined(typeof(GameModePhase), phase)
            || roundId < RoundId)
        {
            return;
        }

        GameMode nextMode = (GameMode)mode;
        EffectiveMapOverrides = mapOverridesEnabled;
        if (nextMode != GameMode.None
            && !ModeMapCatalog.IsSupported(nextMode, mapName, EffectiveMapOverrides))
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

        if (MyceliumNetwork.IsHost && nextMode != GameMode.None && !IsEnabled(nextMode))
        {
            DebugLog.Info($"ApplyActiveMode rejected disabled host mode={nextMode}");
            EnsureActiveMode();
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
        _roundLifecycleStarted = false;
        _customRoundTransitionPending = false;
        _skipRoundTransitionPending = false;
        PendingDeaths.Clear();
        GameModeRespawn.ResetForMatch();
        RespawnProtection.ResetState();
        ModeTimeoutState.ResetMatchState();
        TeamWeaponLoadouts.ResetMatchState();
        foreach (ModeDescriptor descriptor in Modes.Values)
        {
            descriptor.Reset();
        }
    }

    internal static void ResetGameState()
    {
        DebugLog.Info($"ResetGameState host={MyceliumNetwork.IsHost} mode={ActiveMode} phase={Phase} round={RoundId}");
        _roundLifecycleStarted = false;
        if (Phase == GameModePhase.EndingRound)
        {
            PendingDeaths.Clear();
            GameModeRespawn.ResetForMatch();
            if (MyceliumNetwork.IsHost && (ActiveMode == GameMode.Hardpoint
                || ActiveMode == GameMode.CaptureTheFlag
                || ActiveMode == GameMode.SearchAndDestroy
                || ActiveMode == GameMode.TeamDeathmatch)
                && MyceliumNetwork.InLobby)
            {
                if (ActiveMode == GameMode.Hardpoint)
                {
                    TeamAssignment.AssignForRound();
                }
                else if (ActiveMode == GameMode.CaptureTheFlag)
                {
                    CaptureTheFlagState.PrepareTeamsForRound();
                }
                else if (ActiveMode == GameMode.SearchAndDestroy)
                {
                    SearchAndDestroyState.PrepareTeamsForRound();
                }
                else
                {
                    TeamDeathmatchState.PrepareTeamsForRound();
                }
            }
            return;
        }

        ResetMatchState();
        if (MyceliumNetwork.IsHost)
        {
            if ((ActiveMode == GameMode.Hardpoint || ActiveMode == GameMode.CaptureTheFlag
                || ActiveMode == GameMode.SearchAndDestroy || ActiveMode == GameMode.TeamDeathmatch)
                && MyceliumNetwork.InLobby)
            {
                if (ActiveMode == GameMode.Hardpoint)
                {
                    TeamAssignment.AssignForRound();
                }
                else if (ActiveMode == GameMode.CaptureTheFlag)
                {
                    CaptureTheFlagState.PrepareTeamsForRound();
                }
                else if (ActiveMode == GameMode.SearchAndDestroy)
                {
                    SearchAndDestroyState.PrepareTeamsForRound();
                }
                else
                {
                    TeamDeathmatchState.PrepareTeamsForRound();
                }
            }
            RoundId++;
            Phase = ActiveMode == GameMode.None || !MyceliumNetwork.InLobby
                ? GameModePhase.Inactive
                : GameModePhase.Lobby;
            return;
        }

        Phase = ActiveMode == GameMode.None ? GameModePhase.Inactive : GameModePhase.Lobby;
    }

    internal static bool BeginRound()
    {
        DebugLog.Info($"BeginRound host={MyceliumNetwork.IsHost} mode={ActiveMode} phase={Phase} round={RoundId}");
        if (IsVanillaScene || ActiveMode == GameMode.None)
        {
            return false;
        }

        if (_roundLifecycleStarted)
        {
            return false;
        }

        _roundLifecycleStarted = true;

        Phase = GameModePhase.ActiveRound;
        if (MyceliumNetwork.IsHost)
        {
            RoundId++;
            BroadcastActiveMode();
        }

        ModeTimeoutState.OnRoundStarted();

        return true;
    }

    private static bool HasCapability(GameModeCapabilities capability)
    {
        return Modes.TryGetValue(ActiveMode, out ModeDescriptor? descriptor)
            && descriptor.Capabilities.HasFlag(capability);
    }

    internal static void EnsureVanillaScene()
    {
        if (!IsVanillaScene || (ActiveMode == GameMode.None && Phase == GameModePhase.Inactive))
        {
            return;
        }

        ResetMatchState();
        ActiveMode = GameMode.None;
        Phase = GameModePhase.Inactive;
        RoundId++;
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            BroadcastActiveMode();
        }
    }

    private static void BroadcastActiveMode()
    {
        int revision = Sync.NextLiveRevision();
        DebugLog.Info($"BroadcastActiveMode host={MyceliumNetwork.LobbyHost.m_SteamID} mode={ActiveMode} "
            + $"map={SelectedMapName} phase={Phase} round={RoundId} revision={revision} players={MyceliumNetwork.PlayerCount}");
        PublishActiveModeSnapshot(revision);
        MyceliumNetwork.RPC(ModId, nameof(Plugin.SyncActiveGameMode), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, (int)ActiveMode, RoundId, (int)Phase, revision,
            SelectedMapName, EffectiveMapOverrides);
    }

    private static void PublishActiveModeSnapshot(int revision)
    {
        string payload = string.Join("|", MyceliumNetwork.LobbyHost.m_SteamID,
            (int)ActiveMode, RoundId, (int)Phase, revision, SelectedMapName,
            EffectiveMapOverrides ? "1" : "0");
        ModeLobbyDataSync.PublishRaw(ActiveModeLobbyDataKey, payload);
    }

    private static void ApplyLobbyActiveModeSnapshot()
    {
        if (!ModeLobbyDataSync.TryReadOrdered(ActiveModeLobbyDataKey, 7, 0, 2, 4,
            out CSteamID hostId, out int roundId, out int revision, out string[] parts)
            || !int.TryParse(parts[1], out int mode)
            || !int.TryParse(parts[3], out int phase)
            || !LobbySnapshotCodec.TryParseBool(parts[6], out bool mapOverridesEnabled))
        {
            return;
        }

        if (!Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, "active-mode-lobby-data"))
        {
            return;
        }

        ApplyActiveMode(mode, roundId, phase, parts[5], mapOverridesEnabled);
        DebugLog.Info($"[GameMode] Accepted active mode via lobby data: mode={(GameMode)mode} "
            + $"map={parts[5]} round={roundId} phase={(GameModePhase)phase} "
            + $"overrides={mapOverridesEnabled} revision={revision}");
    }

    private static readonly HashSet<int> PendingDeaths = new();
    private static bool _customRoundTransitionPending;
    private static bool _skipRoundTransitionPending;
    private const int NoWinningTeamId = int.MinValue;

    internal static void CompleteCustomRound(int winningTeamId, bool awardRoundPoint = true)
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
        if (awardRoundPoint)
        {
            ScoreManager.Instance.AddPoints(winningTeamId);
        }
        RoundManager.Instance.CmdEndRound(winningTeamId);
        Plugin.Instance.StartCoroutine(AdvanceAfterCustomRound(roundId));
    }

    internal static void SkipCurrentRound()
    {
        if (!MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby || !IsCustomMode
            || Phase != GameModePhase.ActiveRound || _customRoundTransitionPending
            || RoundManager.Instance == null || ScoreManager.Instance == null
            || SceneMotor.Instance == null || Plugin.Instance == null)
        {
            return;
        }

        _customRoundTransitionPending = true;
        _skipRoundTransitionPending = true;
        Phase = GameModePhase.EndingRound;
        BroadcastActiveMode();
        int roundId = RoundId;

        ScoreManager.Instance.ResetRound();
        RoundManager.Instance.CmdEndRound(NoWinningTeamId);
        Plugin.Logger.LogInfo($"[GameMode] Skipping round without points: mode={ActiveMode} "
            + $"map={SelectedMapName} round={roundId}");
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
                if (HardpointState.CanRespawn())
                {
                    GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                }
                break;
            case GameMode.CaptureTheFlag:
                CaptureTheFlagState.OnServerKill(playerId, killerId);
                if (CaptureTheFlagState.CanRespawn())
                {
                    GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                }
                break;
            case GameMode.SearchAndDestroy:
                SearchAndDestroyState.OnServerKill(playerId, killerId);
                break;
            case GameMode.TeamDeathmatch:
                TeamDeathmatchState.OnServerKill(playerId, killerId);
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
                && method.GetParameters() is { Length: 2 } parameters
                && parameters[0].ParameterType == typeof(int)
                && parameters[1].ParameterType == typeof(uint));
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

[HarmonyLib.HarmonyPatch]
internal static class GameManager_RecordPlayerDeath_Patch
{
    private static System.Reflection.MethodBase? TargetMethod()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        return typeof(GameManager).GetMethod("RecordPlayerDeath", flags);
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
    public void SyncTakeResult(int resultId, string text, float durationSeconds, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || resultId < 0 || string.IsNullOrWhiteSpace(text)
            || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
        {
            return;
        }

        GameModeHud.ReceiveTakeResult(resultId, text, Mathf.Clamp(durationSeconds, 1f, 8f));
    }

    [CustomRPC]
    public void SyncGlobalSettings(CSteamID hostId, int roundId, int revision, float respawnDelaySeconds,
        int pointsToWin, bool enableMapOverrides, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!GameModeManager.TryAcceptGlobalSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        GameModeManager.ApplyGlobalSettings(respawnDelaySeconds, pointsToWin, enableMapOverrides);
    }

    [CustomRPC]
    public void SyncActiveGameMode(CSteamID hostId, int mode, int roundId, int phase, int revision,
        string mapName, bool mapOverridesEnabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!GameModeManager.TryAcceptActiveModeSnapshot(hostId, roundId, revision, "active-mode-rpc"))
        {
            return;
        }
        GameModeManager.ApplyActiveMode(mode, roundId, phase, mapName, mapOverridesEnabled);
        DebugLog.Info($"[GameMode] Accepted active mode via RPC: mode={(GameMode)mode} "
            + $"map={mapName} round={roundId} phase={(GameModePhase)phase} "
            + $"overrides={mapOverridesEnabled} revision={revision}");
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
        TeammateMarker.ResetState();
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

    private static void Postfix(string __result)
    {
        GameModeManager.OnNormalMapSelected(__result);
    }
}

[HarmonyLib.HarmonyPatch(typeof(SceneMotor), "ServerStartGameScene")]
internal static class SceneMotor_GameModeStart_Patch
{
    private static void Prefix(SceneMotor __instance)
    {
        if (GameModeManager.TryPrepareInitialMap(out string mapName))
        {
            __instance.PlayListMaps.Clear();
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