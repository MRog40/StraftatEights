using System.Collections;
using MyceliumNetworking;
using Steamworks;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNetSceneLoadData = FishNet.Managing.Scened.SceneLoadData;
using FishNetReplaceOption = FishNet.Managing.Scened.ReplaceOption;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eights;

internal enum GameMode
{
    None = 0,
    Ffatat = 1,
    Juggertat = 2,
    Guntat = 3,
    Snipertat = 4,
    Straftat = 5,
    Michaeltat = 6,
    Ratatat = 7,
    Chambertat = 8,
    Potatotat = 9,
    Infideltat = 10,
    Hvtat = 11,
    Assassintat = 12,
    Hardtat = 13,
    Capturetat = 14,
    Sndtat = 15,
    Tdmtat = 16,
    Infectedtat = 17,
    Ninjatat = 18,
    Hunttat = 19,
    Tanktat = 20,
    Nifetat = 21,
    PotatoInftat = 22,
    Countertat = 23
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
    internal const string ActiveModeLobbyDataKey = "Eights_ActiveMode";
    private const float NativeRoundEndDurationSeconds = 4f;
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
        GameMode.Straftat,
        GameMode.Ffatat,
        GameMode.Nifetat,
        GameMode.Juggertat,
        GameMode.Guntat,
        GameMode.Snipertat,
        GameMode.Michaeltat,
        GameMode.Ratatat,
        GameMode.Chambertat,
        GameMode.Potatotat,
        GameMode.Infideltat,
        GameMode.Hvtat,
        GameMode.Infectedtat,
        GameMode.PotatoInftat,
        GameMode.Assassintat,
        GameMode.Hardtat,
        GameMode.Capturetat,
        GameMode.Sndtat,
        GameMode.Countertat,
        GameMode.Tdmtat,
        GameMode.Ninjatat,
        GameMode.Hunttat,
        GameMode.Tanktat
    };

    private const string ScoreboardAccentColor = "B7F47A";
    private const int MinimumPointsToWin = 1;
    private const int MaximumPointsToWin = 1000;

    private static readonly Dictionary<GameMode, ModeDescriptor> Modes = new()
    {
        [GameMode.Straftat] = new ModeDescriptor("Straftat", new Color32(220, 220, 220, 255),
            () => Plugin.StraftatEnabled.Value, StraftatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.IgnoreGlobalMovement
            | GameModeCapabilities.SafeRespawn,
            StraftatState.PeriodicPushIfHost,
            pollLiveState: StraftatState.PollLiveStateIfClient),
        [GameMode.Ffatat] = new ModeDescriptor("Ffatat", new Color32(85, 204, 255, 255),
            () => Plugin.FfatatEnabled.Value, FfatatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
                FfatatState.PeriodicPushIfHost, TeamWeaponLoadouts.EnsureLoadouts,
                periodicSettingsPush: FfatatState.PeriodicPushSettingsIfHost,
                    pollLiveState: FfatatState.PollLiveStateIfClient),
        [GameMode.Nifetat] = new ModeDescriptor("Nifetat", new Color32(180, 180, 180, 255),
            () => Plugin.NifetatEnabled.Value, NifetatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.SafeRespawn,
            NifetatState.PeriodicPushIfHost, NifetatState.EnsureLoadouts,
            NifetatState.PeriodicPushSettingsIfHost, NifetatState.PollLiveStateIfClient),
        [GameMode.Juggertat] = new ModeDescriptor("Juggertat", new Color32(255, 106, 0, 255),
            () => Plugin.JuggertatEnabled.Value, JuggertatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.SafeRespawn,
            JuggertatState.PeriodicPushIfHost, JuggertatState.EnsureLoadout,
            JuggertatState.PeriodicPushSettingsIfHost,
            JuggertatState.PollLiveStateIfClient),
        [GameMode.Guntat] = new ModeDescriptor("Guntat", new Color32(255, 221, 85, 255),
            () => Plugin.GuntatEnabled.Value, GuntatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
               GuntatState.PeriodicPushIfHost, GuntatState.EnsureLoadouts,
                    periodicSettingsPush: GuntatState.PeriodicPushSettingsIfHost,
                    pollLiveState: GuntatState.PollLiveStateIfClient),
        [GameMode.Snipertat] = new ModeDescriptor("Snipertat", new Color32(255, 96, 128, 255),
            () => Plugin.SnipertatEnabled.Value, SnipertatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.ClearOutlines
            | GameModeCapabilities.SafeRespawn,
               SnipertatState.PeriodicPushIfHost, SnipertatState.EnsureLoadouts,
                    SnipertatState.PeriodicPushSettingsIfHost,
                    SnipertatState.PollLiveStateIfClient),
        [GameMode.Michaeltat] = new ModeDescriptor("Michaeltat", new Color32(204, 34, 34, 255),
            () => Plugin.MichaeltatEnabled.Value, MichaeltatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.HideHud,
            MichaeltatPeriodicPush, MichaeltatState.EnsureLoadouts,
            MichaeltatState.PeriodicPushSettingsIfHost,
            MichaeltatState.PollLiveStateIfClient),
        [GameMode.Ratatat] = new ModeDescriptor("Ratatat", new Color32(170, 170, 170, 255),
            () => Plugin.RatatatEnabled.Value, RatatatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
            RatatatState.PeriodicPushIfHost, RatatatState.EnsureLoadouts,
            RatatatState.PeriodicPushSettingsIfHost,
            RatatatState.PollLiveStateIfClient),
        [GameMode.Chambertat] = new ModeDescriptor("Chambertat", new Color32(180, 180, 180, 255),
            () => Plugin.ChambertatEnabled.Value, ChambertatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.SafeRespawn,
            ChambertatState.PeriodicPushIfHost, ChambertatState.EnsureLoadouts,
            ChambertatState.PeriodicPushSettingsIfHost,
            ChambertatState.PollLiveStateIfClient),
        [GameMode.Potatotat] = new ModeDescriptor("Potatotat", new Color32(255, 170, 70, 255),
            () => Plugin.PotatotatEnabled.Value, PotatotatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
            PotatotatState.PeriodicPushIfHost, PotatotatState.EnsureLoadouts,
            PotatotatState.PeriodicPushSettingsIfHost,
            PotatotatState.PollLiveStateIfClient),
        [GameMode.Infideltat] = new ModeDescriptor("Infideltat", new Color32(204, 64, 64, 255),
            () => Plugin.InfideltatEnabled.Value, InfideltatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.SafeRespawn,
            InfideltatState.PeriodicPushIfHost, InfideltatState.EnsureLoadouts,
            InfideltatState.PeriodicPushSettingsIfHost,
            InfideltatState.PollLiveStateIfClient),
        [GameMode.Hvtat] = new ModeDescriptor("Hvtat", new Color32(0, 0, 255, 255),
            () => Plugin.HvtatEnabled.Value, HvtatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.SafeRespawn,
            HvtatState.PeriodicPushIfHost, periodicSettingsPush: HvtatState.PeriodicPushSettingsIfHost,
            pollLiveState: HvtatState.PollLiveStateIfClient),
        [GameMode.Infectedtat] = new ModeDescriptor("Infectedtat", new Color32(139, 0, 0, 255),
            () => Plugin.InfectedtatEnabled.Value, InfectedtatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.SafeRespawn,
            InfectedtatState.PeriodicPushIfHost, InfectedtatState.EnsureLoadouts,
            InfectedtatState.PeriodicPushSettingsIfHost,
            InfectedtatState.PollLiveStateIfClient),
        [GameMode.PotatoInftat] = new ModeDescriptor("PotatoInftat",
            new Color32(255, 80, 40, 255),
            () => Plugin.PotatoInftatEnabled.Value, PotatoInftatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.SafeRespawn,
            PotatoInftatState.PeriodicPushIfHost, PotatoInftatState.EnsureLoadouts,
            PotatoInftatState.PeriodicPushSettingsIfHost,
            PotatoInftatState.PollLiveStateIfClient),
        [GameMode.Assassintat] = new ModeDescriptor("Assassintat", new Color32(53, 208, 95, 255),
            () => Plugin.AssassintatEnabled.Value, AssassintatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn,
            AssassintatState.PeriodicPushIfHost, AssassintatState.EnsureLoadouts,
            AssassintatState.PeriodicPushSettingsIfHost,
            AssassintatState.PollLiveStateIfClient),
        [GameMode.Hardtat] = new ModeDescriptor("Hardtat", new Color32(0, 114, 178, 255),
            () => Plugin.HardtatEnabled.Value, HardtatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            HardtatState.PeriodicPushIfHost, TeamWeaponLoadouts.EnsureLoadouts,
            HardtatState.PeriodicPushSettingsIfHost, HardtatState.PollLiveStateIfClient),
        [GameMode.Capturetat] = new ModeDescriptor("Capturetat", new Color32(255, 190, 55, 255),
            () => Plugin.CapturetatEnabled.Value, CapturetatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            CapturetatState.PeriodicPushIfHost, ensureLoadouts: TeamWeaponLoadouts.EnsureLoadouts,
            periodicSettingsPush: CapturetatState.PeriodicPushSettingsIfHost,
            pollLiveState: CapturetatState.PollLiveStateIfClient),
        [GameMode.Sndtat] = new ModeDescriptor("Sndtat", new Color32(225, 70, 70, 255),
            () => Plugin.SndtatEnabled.Value, SndtatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            SndtatState.PeriodicPushIfHost, ensureLoadouts: TeamWeaponLoadouts.EnsureLoadouts,
            periodicSettingsPush: SndtatState.PeriodicPushSettingsIfHost,
            pollLiveState: SndtatState.PollLiveStateIfClient),
        [GameMode.Countertat] = new ModeDescriptor("Countertat", new Color32(80, 170, 235, 255),
            () => Plugin.CountertatEnabled.Value, Noop,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            SndtatState.PeriodicPushIfHost, ensureLoadouts: TeamWeaponLoadouts.EnsureLoadouts,
            periodicSettingsPush: CountertatState.PeriodicPushSettingsIfHost,
            pollLiveState: SndtatState.PollLiveStateIfClient),
        [GameMode.Tdmtat] = new ModeDescriptor("Tdmtat", new Color32(255, 190, 55, 255),
            () => Plugin.TdmtatEnabled.Value, TdmtatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            TdmtatState.PeriodicPushIfHost, ensureLoadouts: TeamWeaponLoadouts.EnsureLoadouts,
            periodicSettingsPush:
            TdmtatState.PeriodicPushSettingsIfHost,
            pollLiveState: TdmtatState.PollLiveStateIfClient),
        [GameMode.Ninjatat] = new ModeDescriptor("Ninjatat", new Color32(152, 91, 224, 255),
            () => Plugin.NinjatatEnabled.Value, HuntersReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            HuntModesState.PeriodicPushIfHost, HuntModesState.EnsureLoadouts,
            HuntModesState.PeriodicPushSettingsIfHost, HuntModesState.PollLiveStateIfClient),
        [GameMode.Hunttat] = new ModeDescriptor("Hunttat", new Color32(238, 156, 196, 255),
            () => Plugin.HunttatEnabled.Value, HuntersReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            HuntModesState.PeriodicPushIfHost, HuntModesState.EnsureLoadouts,
            HuntModesState.PeriodicPushSettingsIfHost, HuntModesState.PollLiveStateIfClient),
        [GameMode.Tanktat] = new ModeDescriptor("Tanktat", new Color32(190, 118, 52, 255),
            () => Plugin.TanktatEnabled.Value, HuntersReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.IgnoreGlobalMovement
            | GameModeCapabilities.SafeRespawn | GameModeCapabilities.TeamBased,
            HuntModesState.PeriodicPushIfHost, HuntModesState.EnsureLoadouts,
            HuntModesState.PeriodicPushSettingsIfHost, HuntModesState.PollLiveStateIfClient)
    };

    private static void StraftatReset() => StraftatState.ResetMatchState();
    private static void Noop() { }
    private static void MichaeltatPeriodicPush()
    {
        MichaeltatState.PeriodicPushSettingsIfHost();
        MichaeltatState.PeriodicPushLiveStateIfHost();
    }
    private static void FfatatReset() => FfatatState.ResetMatchState();
    private static void NifetatReset() => NifetatState.ResetMatchState();
    private static void JuggertatReset() => JuggertatState.ResetMatchState();
    private static void GuntatReset() => GuntatState.ResetMatchState();
    private static void SnipertatReset() => SnipertatState.ResetMatchState();
    private static void MichaeltatReset() => MichaeltatState.ResetMatchState();
    private static void RatatatReset() => RatatatState.ResetMatchState();
    private static void ChambertatReset() => ChambertatState.ResetMatchState();
    private static void PotatotatReset() => PotatotatState.ResetMatchState();
    private static void InfideltatReset() => InfideltatState.ResetMatchState();
    private static void HvtatReset() => HvtatState.ResetMatchState();
    private static void InfectedtatReset() => InfectedtatState.ResetMatchState();
    private static void PotatoInftatReset() => PotatoInftatState.ResetMatchState();
    private static void AssassintatReset() => AssassintatState.ResetMatchState();
    private static void HardtatReset() => HardtatState.ResetMatchState();
    private static void CapturetatReset() => CapturetatState.ResetMatchState();
    private static void SndtatReset() => SndtatState.ResetMatchState();
    private static void HuntersReset() => HuntModesState.ResetMatchState();
    private static void TdmtatReset() => TdmtatState.ResetMatchState();

    internal static GameMode ActiveMode { get; private set; }
    internal static bool IsHuntersMode(GameMode mode)
    {
        return mode == GameMode.Ninjatat || mode == GameMode.Hunttat
            || mode == GameMode.Tanktat;
    }

    internal static bool IsHuntersActive => IsHuntersMode(ActiveMode);
    internal static GameModePhase Phase { get; private set; } = GameModePhase.Inactive;
    internal static int RoundId { get; private set; }
    private static bool _roundLifecycleStarted;
    private static bool _isBulkModeToggle;
    internal static string SelectedMapName { get; private set; } = string.Empty;
    internal static ConfigEntry<float> RespawnDelaySeconds = null!;
    internal static ConfigEntry<int> PreRoundTimerSeconds = null!;
    internal static ConfigEntry<int> PointsToWin = null!;
    internal static ConfigEntry<bool> EnableMapOverrides = null!;
    internal static ConfigEntry<bool> KeepTeams = null!;
    internal static float EffectiveRespawnDelaySeconds { get; set; } = 2.5f;
    private static int _configuredPreRoundSeconds = 5;
    internal static int EffectivePreRoundSeconds { get; private set; } = 5;
    internal static int EffectivePointsToWin { get; private set; } = ScoreRules.PointsToWin;
    internal static bool EffectiveMapOverrides { get; private set; }
    internal static bool EffectiveKeepTeams { get; private set; }
    private static readonly ModeSyncState Sync = new();
    private static int _lastRoundEndCountdownSeconds = -1;
    private static readonly Dictionary<GameMode, Queue<string>> RecentMapsByMode = new();
    private const int RecentMapHistorySize = 2;
    private static List<MapPlaylistEntry<GameMode>> _mapPlaylist = new();
    private static System.Random? _mapPlaylistRandom;
    private static int _mapPlaylistIndex = -1;
    private static bool _mapPlaylistPrepared;
    private static float _nextClientLobbyPollTime;
    private static string _pendingNormalMapName = string.Empty;

    internal static void Initialize()
    {
        EnableMapOverrides = Plugin.Instance.Config.Bind("Global Settings", "Enable Map Overrides", true,
            "Host-controlled: use the plugin's mode-specific map overrides instead of the normal lobby map playlist.");
        EnableMapOverrides.SettingChanged += (_, _) => OnGlobalSettingsChanged();
        RespawnDelaySeconds = Plugin.Instance.Config.Bind("Global Settings", "Respawn Delay (seconds)", 2.5f,
            new ConfigDescription("Host-controlled: how long a killed player waits before respawning. Team-based modes with respawns (Capturetat, Hardtat, and Tdmtat) use twice this delay for balance.",
                new AcceptableValueRange<float>(0f, 10f)));
        RespawnDelaySeconds.SettingChanged += (_, _) => OnGlobalSettingsChanged();
        PreRoundTimerSeconds = Plugin.Instance.Config.Bind("Global Settings", "Pre-round Timer (seconds)", 5,
            new ConfigDescription("Host-controlled: Ffatat modes use this setting; Team modes use 2x this setting.",
                new AcceptableValueRange<int>(0, 15)));
        PreRoundTimerSeconds.SettingChanged += (_, _) => OnGlobalSettingsChanged();
        PointsToWin = Plugin.Instance.Config.Bind("Global Settings", "Points To Win", ScoreRules.PointsToWin,
            new ConfigDescription("Host-controlled: score limit for all point-based game modes.",
                new AcceptableValueRange<int>(MinimumPointsToWin, MaximumPointsToWin)));
        PointsToWin.SettingChanged += (_, _) => OnGlobalSettingsChanged();
        KeepTeams = Plugin.Instance.Config.Bind("Global Settings", "Keep Teams", false,
            "Host-controlled: keep the same team layout between rounds when possible.");
        KeepTeams.SettingChanged += (_, _) => OnGlobalSettingsChanged();
        Plugin.PlayerRadarEnabled = Plugin.Instance.Config.Bind("Global Settings", "Player Radar Enabled", true,
            "Host-controlled: show the player radar in custom game modes.");

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
            bool previousKeepTeams = EffectiveKeepTeams;
            ApplyGlobalSettingsFromHostConfig();
            if (!previousKeepTeams && EffectiveKeepTeams)
            {
                TeamAssignment.CaptureCurrentLayout();
            }
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
        ApplyGlobalSettings(RespawnDelaySeconds.Value, PreRoundTimerSeconds.Value,
            PointsToWin.Value, EnableMapOverrides.Value, KeepTeams.Value);
    }

    internal static void ApplyGlobalSettings(float respawnDelaySeconds, int preRoundSeconds,
        int pointsToWin, bool enableMapOverrides, bool keepTeams)
    {
        EffectiveRespawnDelaySeconds = Mathf.Clamp(respawnDelaySeconds, 0f, 10f);
        _configuredPreRoundSeconds = Mathf.Clamp(preRoundSeconds, 0, 15);
        RecalculateEffectivePreRoundSeconds();
        EffectiveMapOverrides = enableMapOverrides;
        EffectiveKeepTeams = keepTeams;
        int nextPointsToWin = Mathf.Clamp(pointsToWin, MinimumPointsToWin, MaximumPointsToWin);
        if (EffectivePointsToWin != nextPointsToWin)
        {
            EffectivePointsToWin = nextPointsToWin;
            if (MyceliumNetwork.IsHost && MyceliumNetwork.InLobby
                && Phase == GameModePhase.ActiveRound && ActiveMode != GameMode.None
                && IsCustomMode && !IsMatchOver && Plugin.Instance != null)
            {
                ResetMatchState();
                RestartRoundAfterModeDisabled();
            }
            else
            {
                ResetPointModeStates();
            }
        }
    }

    private static void ResetPointModeStates()
    {
        ModeTimeoutState.ResetMatchState();
        StraftatState.ResetMatchState();
        FfatatState.ResetMatchState();
        JuggertatState.ResetMatchState();
        GuntatState.ResetMatchState();
        SnipertatState.ResetMatchState();
        HvtatState.ResetMatchState();
        AssassintatState.ResetMatchState();
        HardtatState.ResetMatchState();
        CapturetatState.ResetMatchState();
        SndtatState.ResetMatchState();
        MichaeltatState.ResetMatchState();
        RatatatState.ResetMatchState();
        ChambertatState.ResetMatchState();
        PotatotatState.ResetMatchState();
        InfideltatState.ResetMatchState();
        InfectedtatState.ResetMatchState();
        PotatoInftatState.ResetMatchState();
        NifetatState.ResetMatchState();
        TdmtatState.ResetMatchState();
    }

    private static void BroadcastGlobalSettings()
    {
        MyceliumNetwork.RPC(ModId, nameof(Plugin.SyncGlobalSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, RoundId, Sync.NextSettingsRevision(),
            EffectiveRespawnDelaySeconds, _configuredPreRoundSeconds, EffectivePointsToWin,
            EffectiveMapOverrides, EffectiveKeepTeams);
    }

    internal static void ToggleAllModes()
    {
        ConfigEntry<bool>[] modeSettings =
        {
            Plugin.StraftatEnabled,
            Plugin.FfatatEnabled,
            Plugin.NifetatEnabled,
            Plugin.JuggertatEnabled,
            Plugin.GuntatEnabled,
            Plugin.SnipertatEnabled,
            Plugin.MichaeltatEnabled,
            Plugin.RatatatEnabled,
            Plugin.ChambertatEnabled,
            Plugin.PotatotatEnabled,
            Plugin.InfideltatEnabled,
            Plugin.HvtatEnabled,
            Plugin.InfectedtatEnabled,
            Plugin.PotatoInftatEnabled,
            Plugin.AssassintatEnabled,
            Plugin.HardtatEnabled,
            Plugin.CapturetatEnabled,
            Plugin.SndtatEnabled,
            Plugin.TdmtatEnabled,
            Plugin.NinjatatEnabled,
            Plugin.HunttatEnabled,
            Plugin.TanktatEnabled
        };
        bool enableAll = GameModeToggleRules.ShouldEnableAll(
            modeSettings.Select(setting => setting.Value));

        _isBulkModeToggle = true;
        try
        {
            foreach (ConfigEntry<bool> setting in modeSettings)
            {
                if (setting.Value != enableAll)
                {
                    setting.Value = enableAll;
                }
            }
        }
        finally
        {
            _isBulkModeToggle = false;
        }

        OnSettingsChanged();
    }

    internal static void OnSettingsChanged()
    {
        if (_isBulkModeToggle)
        {
            return;
        }

        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost && !IsMatchOver)
        {
            bool activeModeDisabled = Phase == GameModePhase.ActiveRound
                && ActiveMode != GameMode.None && !IsEnabled(ActiveMode);
            if (!activeModeDisabled)
            {
                EnsureActiveMode();
            }
            AddNewConfiguredModesToPlaylist();
        }
    }

    private static void RestartRoundAfterModeDisabled()
    {
        if (Plugin.Instance == null)
        {
            return;
        }

        TeamAssignment.EnsureAssignedForActiveRound();
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
            case GameMode.Straftat:
                StraftatState.OnRoundStarted();
                break;
            case GameMode.Michaeltat:
                MichaeltatState.OnRoundStarted();
                break;
            case GameMode.Chambertat:
                ChambertatState.OnRoundStarted();
                break;
            case GameMode.Potatotat:
                PotatotatState.OnRoundStarted();
                break;
            case GameMode.Infideltat:
                InfideltatState.OnRoundStarted();
                break;
            case GameMode.Assassintat:
                AssassintatState.OnRoundStarted();
                break;
            case GameMode.Hardtat:
                HardtatState.OnRoundStarted();
                break;
            case GameMode.Capturetat:
                CapturetatState.OnRoundStarted();
                break;
            case GameMode.Sndtat:
            case GameMode.Countertat:
                SndtatState.OnRoundStarted();
                break;
            case GameMode.Tdmtat:
                TdmtatState.OnRoundStarted();
                break;
            case GameMode.Ninjatat:
            case GameMode.Hunttat:
            case GameMode.Tanktat:
                HuntModesState.OnRoundStarted();
                break;
            case GameMode.Infectedtat:
                InfectedtatState.OnRoundStarted();
                break;
            case GameMode.PotatoInftat:
                PotatoInftatState.OnRoundStarted();
                break;
            case GameMode.Nifetat:
                NifetatState.OnRoundStarted();
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

        _nextClientLobbyPollTime = Time.unscaledTime
            + HostSettingsSync.SettingsHeartbeatIntervalSeconds;
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
        if (IsVanillaScene)
        {
            EnsureVanillaScene();
            return false;
        }

        if (!MyceliumNetwork.IsHost)
        {
            return false;
        }

        if (_mixupTransitionPending)
        {
            _mixupTransitionPending = false;
            ResetMatchState();
            RoundId++;
            Phase = GameModePhase.Lobby;
            PrepareTeamsForCurrentMode();
            BroadcastActiveMode();
            return TryLoadSelectedMap();
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

    private static void PrepareTeamsForCurrentMode()
    {
        if (!MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby)
        {
            return;
        }

        if (ActiveMode == GameMode.Hardtat)
        {
            TeamAssignment.AssignForRound();
        }
        else if (ActiveMode == GameMode.Capturetat)
        {
            CapturetatState.PrepareTeamsForRound();
        }
        else if (IsBombMode(ActiveMode))
        {
            SndtatState.PrepareTeamsForRound();
        }
        else if (IsHuntersMode(ActiveMode))
        {
            HuntModesState.PrepareTeamsForRound();
        }
        else if (ActiveMode == GameMode.Tdmtat)
        {
            TdmtatState.PrepareTeamsForRound();
        }
    }

    internal static void StartMatch()
    {
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
            if (client != null && ScoreManager.Instance.GetPoints(TeamAssignment.ResolveTeamId(client.PlayerId))
                >= SceneMotor.Instance.roundAmount)
            {
                return true;
            }
        }

        return false;
    }

    private static void EndMatch()
    {
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

    internal static bool IsBombMode(GameMode mode)
    {
        return mode == GameMode.Sndtat || mode == GameMode.Countertat;
    }

    internal static bool IsBombModeActive => IsBombMode(ActiveMode) && !IsVanillaScene;

    internal static bool IsCurrentRoundMode(GameMode mode)
    {
        return IsActive(mode)
            && (Phase == GameModePhase.ActiveRound || Phase == GameModePhase.EndingRound);
    }

    internal static bool ShouldDeferModeDisable(GameMode mode, bool enabled)
    {
        return !enabled && IsCurrentRoundMode(mode);
    }

    internal static bool IsModeEnabledForCurrentRound(GameMode mode, bool configuredEnabled)
    {
        return configuredEnabled || IsCurrentRoundMode(mode);
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
    internal static float GetRespawnDelay(float baseDelay)
    {
        return IsTeamBased ? baseDelay * 2f : baseDelay;
    }
    internal static bool UsesTeamWeaponLoadouts => IsTeamBased || IsActive(GameMode.Ffatat);
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
            (ActiveMode == GameMode.Juggertat && JuggertatState.IsCurrentJuggertatWeapon(weapon));
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

        string label = labelOverride ?? descriptor.Label;
        return $"<b><color=#{ColorUtility.ToHtmlStringRGB(descriptor.Color)}>{label}</color></b>";
    }

    internal static string GetScoreboardModeLabelMarkup(GameMode mode, string? labelOverride)
    {
        if (!Modes.TryGetValue(mode, out ModeDescriptor? descriptor))
        {
            return "<b>UNKNOWN</b>";
        }

        string label = labelOverride ?? descriptor.Label;
        return $"<b><color=#{ScoreboardAccentColor}>{label}</color></b>";
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
        Sync.ResetForLobby();
        SessionState.BeginLobby();
        DistributionRandom.ResetForLobby();
        TeamAssignment.ResetDistributionHistory();
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
        SessionState.EndLobby();
        ResetMatchState();
        DistributionRandom.ResetForLobby();
        TeamAssignment.ResetDistributionHistory();
        ActiveMode = GameMode.None;
        Phase = GameModePhase.Inactive;
        RoundId++;
        ResetMapPlaylist();
        _nextClientLobbyPollTime = 0f;
        EffectiveRespawnDelaySeconds = 2.5f;
        _configuredPreRoundSeconds = 5;
        EffectivePreRoundSeconds = 5;
        EffectivePointsToWin = ScoreRules.PointsToWin;
        EffectiveMapOverrides = true;
        EffectiveKeepTeams = false;
        GlobalModifiersState.ResetForLobbyLeft();
        HealthSettingsState.ResetForLobbyLeft();
        WeaponSettingsState.ResetForLobbyLeft();
        WeaponService.ResetPendingRequests();
        GameModeRespawn.ResetForLobbyLeft();
        RespawnProtection.ResetState();
    }

    private static void OnPlayerEntered(CSteamID player)
    {
        if (MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPCTarget(ModId, nameof(Plugin.SyncGlobalSettings), player,
                ReliableType.Reliable, MyceliumNetwork.LobbyHost, RoundId, Sync.SettingsRevision,
                EffectiveRespawnDelaySeconds, _configuredPreRoundSeconds, EffectivePointsToWin,
                EffectiveMapOverrides, EffectiveKeepTeams);
            MyceliumNetwork.RPCTarget(ModId, nameof(Plugin.SyncActiveGameMode), player,
                ReliableType.Reliable, MyceliumNetwork.LobbyHost, (int)ActiveMode, RoundId,
                (int)Phase, Sync.LiveRevision, SelectedMapName, EffectiveMapOverrides);
            ModeTimeoutState.OnPlayerEntered(player);
        }
    }

    private static GameMode NextEnabledMode(GameMode current)
    {
        List<GameMode> modes = GetConfiguredModes();
        List<GameMode> candidates = modes.Where(mode => mode != current).ToList();
        if (candidates.Count == 0)
        {
            return modes.Count == 1 && modes[0] == current ? current : GameMode.None;
        }

        return DistributionRandom.SelectMode("GameMode", candidates);
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
        _mapPlaylistPrepared = true;
        foreach (MapPlaylistEntry<GameMode> entry in _mapPlaylist)
        {
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

            RecentMapsByMode.TryGetValue(mode, out Queue<string>? recentMaps);
            string mapName = MapPlaylist.SelectNextMap(
                ModeMapCatalog.GetMapNames(mode, EffectiveMapOverrides), string.Empty,
                _mapPlaylistRandom, recentMaps);
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
        RecentMapsByMode.TryGetValue(entry.Mode, out Queue<string>? recentMaps);
        string previousMap = GetLatestRecentMap(recentMaps);
        if (_mapPlaylistIndex >= 0)
        {
            mapName = MapPlaylist.SelectNextMap(
                ModeMapCatalog.GetMapNames(entry.Mode, EffectiveMapOverrides), previousMap,
                _mapPlaylistRandom, recentMaps);
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
        RecordRecentMap(entry.Mode, mapName);
        SelectedMapName = mapName;
        return true;
    }

    private static void RecordRecentMap(GameMode mode, string mapName)
    {
        if (!RecentMapsByMode.TryGetValue(mode, out Queue<string>? recentMaps))
        {
            recentMaps = new Queue<string>();
            RecentMapsByMode[mode] = recentMaps;
        }

        if (GetLatestRecentMap(recentMaps) == mapName)
        {
            return;
        }

        recentMaps.Enqueue(mapName);
        while (recentMaps.Count > RecentMapHistorySize)
        {
            recentMaps.Dequeue();
        }
    }

    private static string GetLatestRecentMap(Queue<string>? recentMaps)
    {
        if (recentMaps == null || recentMaps.Count == 0)
        {
            return string.Empty;
        }

        string latestMap = string.Empty;
        foreach (string mapName in recentMaps)
        {
            latestMap = mapName;
        }

        return latestMap;
    }

    private static void SetDefaultMapForMode(GameMode mode)
    {
        SelectedMapName = string.Empty;
        IReadOnlyList<string> mapNames = ModeMapCatalog.GetMapNames(mode, EffectiveMapOverrides);
        if (mapNames.Count == 0)
        {
            return;
        }

        _mapPlaylistRandom ??= new System.Random(UnityEngine.Random.Range(0, int.MaxValue));
        string mapName = MapPlaylist.SelectNextMap(mapNames, string.Empty, _mapPlaylistRandom);
        if (ModeMapCatalog.IsSupported(mode, mapName, EffectiveMapOverrides))
        {
            SelectedMapName = mapName;
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
        RecentMapsByMode.Clear();
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

        List<GameMode> candidates = compatibleModes.Where(mode => mode != current).ToList();
        if (candidates.Count == 0)
        {
            return compatibleModes.Count == 1 && compatibleModes[0] == current
                ? current
                : GameMode.None;
        }

        return DistributionRandom.SelectMode("GameMode", candidates);
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
        if (MyceliumNetwork.IsHost && mode != GameMode.None && !IsEnabled(mode))
        {
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
        RecalculateEffectivePreRoundSeconds();
        Phase = MyceliumNetwork.InLobby ? GameModePhase.Lobby : GameModePhase.Inactive;
        RoundId++;
        if ((mode == GameMode.Hardtat || mode == GameMode.Capturetat
            || IsBombMode(mode) || mode == GameMode.Tdmtat
            || IsHuntersMode(mode))
            && MyceliumNetwork.IsHost)
        {
            if (mode == GameMode.Hardtat)
            {
                TeamAssignment.AssignForRound();
            }
            else if (mode == GameMode.Capturetat)
            {
                CapturetatState.PrepareTeamsForRound();
            }
            else if (IsBombMode(mode))
            {
                SndtatState.PrepareTeamsForRound();
            }
            else if (IsHuntersMode(mode))
            {
                HuntModesState.PrepareTeamsForRound();
            }
            else
            {
                TdmtatState.PrepareTeamsForRound();
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

        bool preserveDisabledActiveMode = nextMode == ActiveMode
            && (Phase == GameModePhase.ActiveRound || Phase == GameModePhase.EndingRound)
            && ((GameModePhase)phase == GameModePhase.ActiveRound
                || (GameModePhase)phase == GameModePhase.EndingRound);
        if (MyceliumNetwork.IsHost && nextMode != GameMode.None && !IsEnabled(nextMode)
            && !preserveDisabledActiveMode)
        {
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
            RecalculateEffectivePreRoundSeconds();
        }

        RoundId = roundId;
        Phase = (GameModePhase)phase;
        SelectedMapName = nextMode == GameMode.None ? string.Empty : mapName;
    }

    internal static void ResetMatchState()
    {
        _roundLifecycleStarted = false;
        _customRoundTransitionPending = false;
        _skipRoundTransitionPending = false;
        _lastRoundEndCountdownSeconds = -1;
        PendingDeaths.Clear();
        GameModeHud.ClearRoundEndCountdown();
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
        _roundLifecycleStarted = false;
        if (Phase == GameModePhase.EndingRound)
        {
            PendingDeaths.Clear();
            GameModeRespawn.ResetForMatch();
            if (MyceliumNetwork.IsHost && (ActiveMode == GameMode.Hardtat
                || ActiveMode == GameMode.Capturetat
                || IsBombMode(ActiveMode)
                || ActiveMode == GameMode.Tdmtat
                || IsHuntersMode(ActiveMode))
                && MyceliumNetwork.InLobby)
            {
                if (ActiveMode == GameMode.Hardtat)
                {
                    TeamAssignment.AssignForRound();
                }
                else if (ActiveMode == GameMode.Capturetat)
                {
                    CapturetatState.PrepareTeamsForRound();
                }
                else if (IsBombMode(ActiveMode))
                {
                    SndtatState.PrepareTeamsForRound();
                }
                else if (IsHuntersMode(ActiveMode))
                {
                    HuntModesState.PrepareTeamsForRound();
                }
                else
                {
                    TdmtatState.PrepareTeamsForRound();
                }
            }
            return;
        }

        ResetMatchState();
        if (MyceliumNetwork.IsHost)
        {
            if ((ActiveMode == GameMode.Hardtat || ActiveMode == GameMode.Capturetat
                || IsBombMode(ActiveMode) || ActiveMode == GameMode.Tdmtat
                || IsHuntersMode(ActiveMode))
                && MyceliumNetwork.InLobby)
            {
                if (ActiveMode == GameMode.Hardtat)
                {
                    TeamAssignment.AssignForRound();
                }
                else if (ActiveMode == GameMode.Capturetat)
                {
                    CapturetatState.PrepareTeamsForRound();
                }
                else if (IsBombMode(ActiveMode))
                {
                    SndtatState.PrepareTeamsForRound();
                }
                else if (IsHuntersMode(ActiveMode))
                {
                    HuntModesState.PrepareTeamsForRound();
                }
                else
                {
                    TdmtatState.PrepareTeamsForRound();
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

    internal static bool IsNativeRoundStartActive => PauseManager.Instance != null
        && PauseManager.Instance.startRound;

    internal static bool IsPreRoundTimerActive => IsNativeRoundStartActive;

    internal static bool IsRoundGameplayActive => Phase == GameModePhase.ActiveRound
        && !IsNativeRoundStartActive;

    internal static void UpdateRoundEndCountdown()
    {
        if (!TryGetRoundEndingCountdown(out string label, out float timeRemaining)
            || timeRemaining > GameModeHud.RoundEndCountdownSeconds)
        {
            if (_lastRoundEndCountdownSeconds >= 0)
            {
                _lastRoundEndCountdownSeconds = -1;
                GameModeHud.ClearRoundEndCountdown();
            }
            return;
        }

        int secondsRemaining = Mathf.CeilToInt(Mathf.Max(0f, timeRemaining));
        if (secondsRemaining == _lastRoundEndCountdownSeconds)
        {
            return;
        }

        _lastRoundEndCountdownSeconds = secondsRemaining;
        GameModeHud.ShowRoundEndCountdown(label, secondsRemaining);
    }

    internal static bool TryGetRoundEndingCountdown(out string label,
        out float timeRemaining)
    {
        label = "ROUND ENDS IN";
        timeRemaining = 0f;
        if (!IsCustomMode || Phase != GameModePhase.ActiveRound || IsPreRoundTimerActive
            || IsMatchOver)
        {
            return false;
        }

        switch (ActiveMode)
        {
            case GameMode.Straftat:
                label = "TAKE ENDS IN";
                timeRemaining = StraftatState.TimeRemaining;
                return true;
            case GameMode.Michaeltat:
                timeRemaining = MichaeltatState.TimeRemaining;
                return true;
            case GameMode.Infideltat:
                label = "TAKE ENDS IN";
                timeRemaining = InfideltatState.TakeTimeRemaining;
                return true;
            case GameMode.Assassintat:
                label = "TAKE ENDS IN";
                timeRemaining = AssassintatState.TakeTimeRemaining;
                return true;
            case GameMode.Capturetat:
                timeRemaining = CapturetatState.MatchTimeRemaining;
                return true;
            case GameMode.Sndtat:
            case GameMode.Countertat:
                label = "TAKE ENDS IN";
                timeRemaining = SndtatState.BombStatus
                    == SndtatBombStatus.Planted
                    ? SndtatState.FuseTimeRemaining
                    : SndtatState.TakeTimeRemaining;
                return true;
            case GameMode.Ninjatat:
            case GameMode.Hunttat:
            case GameMode.Tanktat:
                label = "TAKE ENDS IN";
                timeRemaining = HuntModesState.IsTieBreakActive
                    ? HuntModesState.TieBreakHoldRemaining
                    : HuntModesState.TakeTimeRemaining;
                return true;
            default:
                if (ModeTimeoutState.IsTimedMode(ActiveMode))
                {
                    timeRemaining = ModeTimeoutState.TimeRemaining;
                    return true;
                }
                return false;
        }
    }

    private static bool HasCapability(GameModeCapabilities capability)
    {
        return Modes.TryGetValue(ActiveMode, out ModeDescriptor? descriptor)
            && descriptor.Capabilities.HasFlag(capability);
    }

    private static void RecalculateEffectivePreRoundSeconds()
    {
        EffectivePreRoundSeconds = _configuredPreRoundSeconds
            * (HasCapability(GameModeCapabilities.TeamBased) ? 2 : 1);
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
    }

    private static readonly HashSet<int> PendingDeaths = new();
    private static bool _customRoundTransitionPending;
    private static bool _skipRoundTransitionPending;
    private static bool _mixupTransitionPending;
    internal const int NoWinningTeamId = int.MinValue;

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

    internal static void CompleteCustomRound(IReadOnlyList<int> winningTeamIds)
    {
        if (!MyceliumNetwork.IsHost || _customRoundTransitionPending || RoundManager.Instance == null
            || ScoreManager.Instance == null || SceneMotor.Instance == null || Plugin.Instance == null
            || winningTeamIds.Count == 0)
        {
            return;
        }

        _customRoundTransitionPending = true;
        Phase = GameModePhase.EndingRound;
        BroadcastActiveMode();
        int roundId = RoundId;

        ScoreManager.Instance.ResetRound();
        HashSet<int> distinctTeams = new();
        foreach (int winningTeamId in winningTeamIds)
        {
            if (winningTeamId >= 0 && distinctTeams.Add(winningTeamId))
            {
                ScoreManager.Instance.AddPoints(winningTeamId);
            }
        }

        RoundManager.Instance.CmdEndRound(winningTeamIds[0]);
        Plugin.Instance.StartCoroutine(AdvanceAfterCustomRound(roundId));
    }

    internal static void SkipCurrentRound()
    {
        if (!MyceliumNetwork.IsHost)
        {
            RequestSkipCurrentRound();
            return;
        }

        bool validPhase = Phase == GameModePhase.Lobby || Phase == GameModePhase.ActiveRound;
        if (!MyceliumNetwork.InLobby || !IsCustomMode || !validPhase
            || _customRoundTransitionPending || RoundManager.Instance == null
            || ScoreManager.Instance == null || SceneMotor.Instance == null || Plugin.Instance == null)
        {
            Plugin.Logger.LogWarning($"[GameMode] Skip round ignored: host={MyceliumNetwork.IsHost} "
                + $"lobby={MyceliumNetwork.InLobby} mode={ActiveMode} phase={Phase} "
                + $"custom={IsCustomMode} pending={_customRoundTransitionPending} "
                + $"roundManager={RoundManager.Instance != null} sceneMotor={SceneMotor.Instance != null}");
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

    internal static void MixupTeams()
    {
        if (!MyceliumNetwork.IsHost)
        {
            RequestMixupTeams();
            return;
        }

        bool validPhase = Phase == GameModePhase.Lobby || Phase == GameModePhase.ActiveRound;
        if (!MyceliumNetwork.InLobby || !IsTeamBased || !validPhase
            || _customRoundTransitionPending || RoundManager.Instance == null
            || ScoreManager.Instance == null || SceneMotor.Instance == null || Plugin.Instance == null
            || !TeamAssignment.QueueMixupForNextRound())
        {
            Plugin.Logger.LogWarning($"[GameMode] Mixup teams ignored: host={MyceliumNetwork.IsHost} "
                + $"lobby={MyceliumNetwork.InLobby} mode={ActiveMode} phase={Phase} "
                + $"teamBased={IsTeamBased} pending={_customRoundTransitionPending}");
            return;
        }

        _customRoundTransitionPending = true;
        _mixupTransitionPending = true;
        Phase = GameModePhase.EndingRound;
        BroadcastActiveMode();
        int roundId = RoundId;

        ScoreManager.Instance.ResetRound();
        RoundManager.Instance.CmdEndRound(NoWinningTeamId);
        Plugin.Logger.LogInfo($"[GameMode] Mixing up teams: mode={ActiveMode} "
            + $"map={SelectedMapName} round={roundId}");
        Plugin.Instance.StartCoroutine(AdvanceAfterCustomRound(roundId));
    }

    private static void RequestSkipCurrentRound()
    {
        if (!MyceliumNetwork.InLobby || ClientInstance.Instance == null)
        {
            return;
        }

        MyceliumNetwork.RPC(ModId, nameof(Plugin.RequestSkipRound), ReliableType.Reliable,
            ClientInstance.Instance.PlayerId);
    }

    private static void RequestMixupTeams()
    {
        if (!MyceliumNetwork.InLobby || ClientInstance.Instance == null)
        {
            return;
        }

        MyceliumNetwork.RPC(ModId, nameof(Plugin.RequestMixupTeams), ReliableType.Reliable,
            ClientInstance.Instance.PlayerId);
    }

    private static IEnumerator AdvanceAfterCustomRound(int roundId)
    {
        int sessionGeneration = SessionState.Generation;
        yield return new WaitForSecondsRealtime(NativeRoundEndDurationSeconds);
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

        if (mode != GameMode.Michaeltat && mode != GameMode.Chambertat
            && mode != GameMode.Assassintat
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
        // Let that RPC finish before resolving the attacker. Melee kills can publish the killer
        // through a later networked step, so keep the death pending while resolving it.
        yield return null;

        if (!SessionState.IsCurrent(sessionGeneration) || ActiveMode != mode || RoundId != roundId
            || Phase == GameModePhase.EndingRound)
        {
            PendingDeaths.Remove(playerId);
            yield break;
        }

        PlayerHealth? deadHealth = null;
        int killerId = -1;
        int maxKillerResolutionAttempts = mode == GameMode.Chambertat ? 12 : 3;
        for (int attempt = 0; attempt < maxKillerResolutionAttempts && killerId < 0; attempt++)
        {
            deadHealth = PlayerLookup.FindPlayerHealthById(playerId);
            killerId = PlayerLookup.FindKillerId(deadHealth);
            if (killerId < 0 && mode == GameMode.Chambertat
                && ChambertatState.TryConsumePendingMeleeKiller(playerId, out int meleeKillerId))
            {
                killerId = meleeKillerId;
            }
            if (killerId < 0)
            {
                yield return null;
            }
        }

        PendingDeaths.Remove(playerId);
        switch (mode)
        {
            case GameMode.Ffatat:
                FfatatState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.Nifetat:
                NifetatState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.Straftat:
                StraftatState.OnServerKill(playerId);
                break;
            case GameMode.Juggertat:
                JuggertatState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.Guntat:
                GuntatState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.Snipertat:
                SnipertatState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.Michaeltat:
                MichaeltatState.OnServerKill(playerId, killerId);
                break;
            case GameMode.Ratatat:
                RatatatState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.Chambertat:
                ChambertatState.OnServerKill(playerId, killerId);
                break;
            case GameMode.Potatotat:
                PotatotatState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.Infideltat:
                InfideltatState.OnServerKill(playerId, killerId);
                break;
            case GameMode.Assassintat:
                AssassintatState.OnServerKill(playerId, killerId);
                break;
            case GameMode.Hardtat:
                HardtatState.OnServerKill(playerId, killerId);
                if (HardtatState.CanRespawn())
                {
                    GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                }
                break;
            case GameMode.Capturetat:
                CapturetatState.OnServerKill(playerId, killerId);
                if (CapturetatState.CanRespawn())
                {
                    GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                }
                break;
            case GameMode.Sndtat:
            case GameMode.Countertat:
                SndtatState.OnServerKill(playerId, killerId);
                break;
            case GameMode.Ninjatat:
            case GameMode.Hunttat:
            case GameMode.Tanktat:
                HuntModesState.OnServerKill(playerId, killerId);
                break;
            case GameMode.Tdmtat:
                TdmtatState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.Hvtat:
                HvtatState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                break;
            case GameMode.Infectedtat:
                InfectedtatState.OnServerKill(playerId, killerId);
                if (!InfectedtatState.IsRoundEnding)
                {
                    GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                }
                break;
            case GameMode.PotatoInftat:
                PotatoInftatState.OnServerKill(playerId, killerId);
                if (!PotatoInftatState.IsRoundEnding)
                {
                    GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
                }
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
    public void RequestSkipRound(int playerId, RPCInfo info)
    {
        if (!MyceliumNetwork.IsHost || !NetworkAuthority.IsPlayerSender(info, playerId))
        {
            return;
        }

        GameModeManager.SkipCurrentRound();
    }

    [CustomRPC]
    public void RequestMixupTeams(int playerId, RPCInfo info)
    {
        if (!MyceliumNetwork.IsHost || !NetworkAuthority.IsPlayerSender(info, playerId))
        {
            return;
        }

        GameModeManager.MixupTeams();
    }

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
    public void SyncGameModeAnnouncement(string text, float durationSeconds, bool showHud,
        RPCInfo info)
    {
        if (MyceliumNetwork.IsHost || !NetworkAuthority.IsHostSender(info)
            || string.IsNullOrWhiteSpace(text)
            || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
        {
            return;
        }

        GameModeHud.ReceiveAnnouncement(text, Mathf.Clamp(durationSeconds, 1f, 8f), showHud);
    }

    [CustomRPC]
    public void SyncTakeResult(int resultId, string text, float durationSeconds, RPCInfo info)
    {
        if (MyceliumNetwork.IsHost || !NetworkAuthority.IsHostSender(info)
            || resultId < 0 || string.IsNullOrWhiteSpace(text)
            || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
        {
            return;
        }

        GameModeHud.ReceiveTakeResult(resultId, text, Mathf.Clamp(durationSeconds, 1f, 8f));
    }

    [CustomRPC]
    public void SyncGlobalSettings(CSteamID hostId, int roundId, int revision, float respawnDelaySeconds,
        int preRoundSeconds, int pointsToWin, bool enableMapOverrides, bool keepTeams, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!GameModeManager.TryAcceptGlobalSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        GameModeManager.ApplyGlobalSettings(respawnDelaySeconds, preRoundSeconds, pointsToWin,
            enableMapOverrides, keepTeams);
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
    }

}

[HarmonyLib.HarmonyPatch(typeof(GameManager), "ResetGame")]
internal static class GameManager_GameModeReset_Patch
{
    private static void Postfix()
    {
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
        GameModeManager.StartMatch();
    }
}