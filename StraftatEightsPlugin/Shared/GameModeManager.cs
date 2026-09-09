using System.Collections;
using MyceliumNetworking;
using Steamworks;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
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
    Infidel = 10
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
    ClearOutlines = 16
}

internal static class GameModeManager
{
    internal const uint ModId = 1618033988u;
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
        GameMode.Infidel
    };

    private static readonly Dictionary<GameMode, ModeDescriptor> Modes = new()
    {
        [GameMode.Default] = new ModeDescriptor("DEFAULT", new Color32(220, 220, 220, 255),
            () => Plugin.DefaultGameModeEnabled.Value, DefaultReset,
            GameModeCapabilities.IgnoreGlobalHealth),
        [GameMode.FreeForAll] = new ModeDescriptor("FFA", new Color32(85, 204, 255, 255),
            () => Plugin.FFAEnabled.Value, FfaReset, GameModeCapabilities.CustomRound,
               FFAState.PeriodicPushIfHost, periodicSettingsPush: FFAState.PeriodicPushSettingsIfHost),
        [GameMode.Juggernaut] = new ModeDescriptor("JUGGERNAUT", new Color32(255, 106, 0, 255),
            () => Plugin.JuggernautEnabled.Value, JuggernautReset, GameModeCapabilities.CustomRound,
            JuggernautState.PeriodicPushIfHost, JuggernautState.EnsureLoadout,
            JuggernautState.PeriodicPushSettingsIfHost),
        [GameMode.GunGame] = new ModeDescriptor("GUN GAME", new Color32(255, 221, 85, 255),
            () => Plugin.GunGameEnabled.Value, GunGameReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons,
               GunGameState.PeriodicPushIfHost, GunGameState.EnsureLoadouts,
               periodicSettingsPush: GunGameState.PeriodicPushSettingsIfHost),
        [GameMode.SniperBattle] = new ModeDescriptor("SNIPER BATTLE", new Color32(255, 96, 128, 255),
            () => Plugin.SniperBattleEnabled.Value, SniperBattleReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.ClearOutlines,
               SniperBattleState.PeriodicPushIfHost, SniperBattleState.EnsureLoadouts,
               SniperBattleState.PeriodicPushSettingsIfHost),
        [GameMode.MichaelMeyers] = new ModeDescriptor("MICHAEL MEYERS", new Color32(204, 34, 34, 255),
            () => Plugin.MichaelMeyersEnabled.Value, MichaelMeyersReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.HideHud | GameModeCapabilities.ClearOutlines,
            MichaelMeyersPeriodicPush, MichaelMeyersState.EnsureLoadouts,
            MichaelMeyersState.PeriodicPushSettingsIfHost),
        [GameMode.KillTheRat] = new ModeDescriptor("KILL THE RAT", new Color32(170, 170, 170, 255),
            () => Plugin.KillTheRatEnabled.Value, KillTheRatReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons,
            KillTheRatState.PeriodicPushIfHost, KillTheRatState.EnsureLoadouts,
            KillTheRatState.PeriodicPushSettingsIfHost),
        [GameMode.OneInTheChamber] = new ModeDescriptor("ONE IN THE CHAMBER", new Color32(180, 180, 180, 255),
            () => Plugin.OneInTheChamberEnabled.Value, OneInTheChamberReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth,
            OneInTheChamberState.PeriodicPushIfHost, OneInTheChamberState.EnsureLoadouts,
            OneInTheChamberState.PeriodicPushSettingsIfHost),
        [GameMode.HotPotato] = new ModeDescriptor("HOT POTATO", new Color32(255, 170, 70, 255),
            () => Plugin.HotPotatoEnabled.Value, HotPotatoReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons,
            HotPotatoState.PeriodicPushIfHost, HotPotatoState.EnsureLoadouts,
            HotPotatoState.PeriodicPushSettingsIfHost),
        [GameMode.Infidel] = new ModeDescriptor("INFIDEL", new Color32(204, 64, 64, 255),
            () => Plugin.InfidelEnabled.Value, InfidelReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth,
            InfidelState.PeriodicPushIfHost, InfidelState.EnsureLoadouts,
            InfidelState.PeriodicPushSettingsIfHost)
    };

    private static void DefaultReset() { }
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

    internal static GameMode ActiveMode { get; private set; }
    internal static GameModePhase Phase { get; private set; } = GameModePhase.Inactive;
    internal static int RoundId { get; private set; }
    internal static ConfigEntry<float> RespawnDelaySeconds = null!;
    internal static ConfigEntry<int> PointsToWin = null!;
    internal static float EffectiveRespawnDelaySeconds { get; set; } = 3f;
    internal static int EffectivePointsToWin { get; private set; } = ScoreRules.PointsToWin;
    private static readonly ModeSyncState Sync = new();

    internal static void Initialize()
    {
        RespawnDelaySeconds = Plugin.Instance.Config.Bind("Global Settings", "Respawn Delay (seconds)", 3f,
            new ConfigDescription("Host-controlled: how long a killed player waits before respawning.",
                new AcceptableValueRange<float>(0f, 10f)));
        RespawnDelaySeconds.SettingChanged += (_, _) => OnGlobalSettingsChanged();
        PointsToWin = Plugin.Instance.Config.Bind("Global Settings", "Points To Win", ScoreRules.PointsToWin,
            "Fixed score limit for all point-based game modes.");
        PointsToWin.Value = ScoreRules.PointsToWin;
        PointsToWin.SettingChanged += (_, _) => OnGlobalSettingsChanged();

        MyceliumNetwork.RegisterNetworkObject(Plugin.Instance, ModId);
        MyceliumNetwork.LobbyCreated += OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += OnLobbyLeft;
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
        FFAState.ResetMatchState();
        JuggernautState.ResetMatchState();
        GunGameState.ResetMatchState();
        SniperBattleState.ResetMatchState();
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
            EnsureActiveMode();
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

    internal static void EnsureActiveModeLoadouts()
    {
        if (Modes.TryGetValue(ActiveMode, out ModeDescriptor? descriptor))
        {
            descriptor.EnsureLoadouts();
        }
    }

    internal static void CycleForNextMap()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        _customRoundTransitionPending = false;
        ActivateMode(NextEnabledMode(ActiveMode), true);
    }

    internal static void HandleSceneChange()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        if (IsFinalMatchTransition())
        {
            EndMatch();
            return;
        }

        CycleForNextMap();
    }

    internal static void StartMatch()
    {
        if (!MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || (ActiveMode != GameMode.None && !IsMatchOver))
        {
            return;
        }

        ActivateMode(NextEnabledMode(GameMode.None), true);
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
        ResetMatchState();
        ActiveMode = GameMode.None;
        Phase = GameModePhase.Inactive;
        RoundId++;
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

    internal static bool IsCustomMode => HasCapability(GameModeCapabilities.CustomRound);
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
        Sync.ResetForLobby();
        SessionState.BeginLobby();
        if (MyceliumNetwork.IsHost)
        {
            ApplyGlobalSettingsFromHostConfig();
            BroadcastGlobalSettings();
            ActivateMode(NextEnabledMode(GameMode.None), true);
        }
    }

    private static void OnLobbyLeft()
    {
        SessionState.EndLobby();
        ResetMatchState();
        ActiveMode = GameMode.None;
        Phase = GameModePhase.Inactive;
        RoundId++;
        EffectiveRespawnDelaySeconds = 3f;
        EffectivePointsToWin = ScoreRules.PointsToWin;
        GlobalModifiersState.ResetForLobbyLeft();
        HealthSettingsState.ResetForLobbyLeft();
        WeaponSettingsState.ResetForLobbyLeft();
        WeaponService.ResetPendingRequests();
        GameModeRespawn.ResetForLobbyLeft();
    }

    private static void OnPlayerEntered(CSteamID player)
    {
        if (MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPCTarget(ModId, nameof(Plugin.SyncGlobalSettings), player,
                ReliableType.Reliable, MyceliumNetwork.LobbyHost, RoundId, Sync.SettingsRevision,
                EffectiveRespawnDelaySeconds, EffectivePointsToWin);
            MyceliumNetwork.RPCTarget(ModId, nameof(Plugin.SyncActiveGameMode), player,
                ReliableType.Reliable, MyceliumNetwork.LobbyHost, (int)ActiveMode, RoundId,
                (int)Phase, Sync.LiveRevision);
        }
    }

    private static GameMode NextEnabledMode(GameMode current)
    {
        List<GameMode> modes = GetConfiguredModes();
        return ModeCycle.TrySelectRandom(modes, current, UnityEngine.Random.Range(0, int.MaxValue), out GameMode next)
            ? next
            : GameMode.None;
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

    internal static bool TryAcceptActiveModeSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptLiveSnapshot(hostId, roundId, revision);
    }

    internal static void ApplyActiveMode(int mode, int roundId, int phase)
    {
        if (!Enum.IsDefined(typeof(GameMode), mode) || !Enum.IsDefined(typeof(GameModePhase), phase)
            || roundId < RoundId)
        {
            return;
        }

        GameMode nextMode = (GameMode)mode;
        if (!HarmonyPatchStatus.IsModeAvailable(nextMode))
        {
            ResetMatchState();
            ActiveMode = GameMode.None;
            Phase = GameModePhase.Inactive;
            RoundId = roundId;
            return;
        }

        bool newRound = roundId > RoundId;
        bool modeChanged = ActiveMode != nextMode || newRound;
        bool phaseChanged = Phase != (GameModePhase)phase;
        if (modeChanged)
        {
            ResetMatchState();
            ActiveMode = nextMode;
        }

        RoundId = roundId;
        Phase = (GameModePhase)phase;
        if (modeChanged || phaseChanged)
        {
            Plugin.Logger.LogInfo($"[GameMode] Applied host state: mode={nextMode} round={roundId} phase={(GameModePhase)phase}");
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
        MyceliumNetwork.RPC(ModId, nameof(Plugin.SyncActiveGameMode), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, (int)ActiveMode, RoundId, (int)Phase, Sync.NextLiveRevision());
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
        Plugin.Logger.LogInfo($"[GameMode] Server death: mode={mode} deadPlayer={playerId} killer={killerId}");

        switch (mode)
        {
            case GameMode.FreeForAll:
                FFAState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, EffectiveRespawnDelaySeconds);
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
    public void SyncScorePopup(int amount)
    {
        if (amount <= 0 || amount > ScoreRules.PointsToWin)
        {
            return;
        }

        GameModeHud.ShowScorePopup(amount);
    }

    [CustomRPC]
    public void SyncGlobalSettings(CSteamID hostId, int roundId, int revision, float respawnDelaySeconds,
        int pointsToWin)
    {
        if (!GameModeManager.TryAcceptGlobalSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        GameModeManager.ApplyGlobalSettings(respawnDelaySeconds, pointsToWin);
    }

    [CustomRPC]
    public void SyncActiveGameMode(CSteamID hostId, int mode, int roundId, int phase, int revision)
    {
        if (!GameModeManager.TryAcceptActiveModeSnapshot(hostId, roundId, revision))
        {
            return;
        }
        GameModeManager.ApplyActiveMode(mode, roundId, phase);
    }
}

[HarmonyLib.HarmonyPatch(typeof(GameManager), "ResetGame")]
internal static class GameManager_GameModeReset_Patch
{
    private static void Postfix()
    {
        GameModeManager.ResetGameState();
        PlayerOutline.ResetState();
        JuggernautOutline.ResetState();
        MichaelMeyersOutline.ResetState();
        KillTheRatOutline.ResetState();
    }
}

[HarmonyLib.HarmonyPatch(typeof(SceneMotor), "ChangeNetworkScene")]
internal static class SceneMotor_GameModeCycle_Patch
{
    private static void Prefix()
    {
        GameModeManager.HandleSceneChange();
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