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
    MichaelMeyers = 6
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
        internal readonly Action PeriodicPush;
        internal readonly Action EnsureLoadouts;

        internal ModeDescriptor(string label, Color color, Func<bool> isEnabled, Action reset,
            GameModeCapabilities capabilities, Action? periodicPush = null, Action? ensureLoadouts = null)
        {
            Label = label;
            Color = color;
            IsEnabled = isEnabled;
            Reset = reset;
            Capabilities = capabilities;
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
        GameMode.MichaelMeyers
    };

    private static readonly Dictionary<GameMode, ModeDescriptor> Modes = new()
    {
        [GameMode.Default] = new ModeDescriptor("DEFAULT", new Color32(220, 220, 220, 255),
            () => Plugin.DefaultGameModeEnabled.Value, DefaultReset,
            GameModeCapabilities.IgnoreGlobalWeapons | GameModeCapabilities.IgnoreGlobalHealth),
        [GameMode.FreeForAll] = new ModeDescriptor("FFA", new Color32(85, 204, 255, 255),
            () => Plugin.FFAEnabled.Value, FfaReset, GameModeCapabilities.CustomRound,
            FFAState.PeriodicPushSettingsIfHost),
        [GameMode.Juggernaut] = new ModeDescriptor("JUGGERNAUT", new Color32(255, 106, 0, 255),
            () => Plugin.JuggernautEnabled.Value, JuggernautReset, GameModeCapabilities.CustomRound,
            JuggernautState.PeriodicPushSettingsIfHost, JuggernautState.EnsureLoadout),
        [GameMode.GunGame] = new ModeDescriptor("GUN GAME", new Color32(255, 221, 85, 255),
            () => Plugin.GunGameEnabled.Value, GunGameReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons,
            GunGameState.PeriodicPushSettingsIfHost),
        [GameMode.SniperBattle] = new ModeDescriptor("SNIPER BATTLE", new Color32(255, 96, 128, 255),
            () => Plugin.SniperBattleEnabled.Value, SniperBattleReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.IgnoreGlobalHealth | GameModeCapabilities.ClearOutlines,
            SniperBattleState.PeriodicPushSettingsIfHost, SniperBattleState.EnsureLoadouts),
        [GameMode.MichaelMeyers] = new ModeDescriptor("MICHAEL MEYERS", new Color32(204, 34, 34, 255),
            () => Plugin.MichaelMeyersEnabled.Value, MichaelMeyersReset,
            GameModeCapabilities.CustomRound | GameModeCapabilities.IgnoreGlobalWeapons
            | GameModeCapabilities.HideHud | GameModeCapabilities.ClearOutlines,
            MichaelMeyersPeriodicPush, MichaelMeyersState.EnsureLoadouts)
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

    internal static GameMode ActiveMode { get; private set; }
    internal static GameModePhase Phase { get; private set; } = GameModePhase.Inactive;
    internal static int RoundId { get; private set; }
    internal static ConfigEntry<float> RespawnDelaySeconds = null!;
    internal static float EffectiveRespawnDelaySeconds { get; set; } = 3f;

    internal static void Initialize()
    {
        RespawnDelaySeconds = Plugin.Instance.Config.Bind("Global Settings", "Respawn Delay (seconds)", 3f,
            new ConfigDescription("Host-controlled: how long a killed player waits before respawning.",
                new AcceptableValueRange<float>(0f, 10f)));
        RespawnDelaySeconds.SettingChanged += (_, _) => OnGlobalSettingsChanged();

        MyceliumNetwork.RegisterNetworkObject(Plugin.Instance, ModId);
        MyceliumNetwork.LobbyCreated += OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += OnLobbyEntered;
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
        EffectiveRespawnDelaySeconds = RespawnDelaySeconds.Value;
    }

    private static void BroadcastGlobalSettings()
    {
        MyceliumNetwork.RPC(ModId, nameof(Plugin.SyncGlobalSettings), ReliableType.Reliable,
            EffectiveRespawnDelaySeconds);
    }

    internal static void OnSettingsChanged()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost && !IsMatchOver)
        {
            EnsureActiveMode();
        }
    }

    private static float _nextModePushTime;

    internal static void PeriodicPushIfHost()
    {
        if (!HostSettingsSync.IsDue(ref _nextModePushTime))
        {
            return;
        }
        ApplyGlobalSettingsFromHostConfig();
        BroadcastGlobalSettings();
        BroadcastActiveMode();
        PeriodicActiveModePushIfHost();
    }

    private static void PeriodicActiveModePushIfHost()
    {
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

    internal static string GetModeLabel(GameMode mode)
    {
        return Modes.TryGetValue(mode, out ModeDescriptor? descriptor) ? descriptor.Label : "UNKNOWN";
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
        if (MyceliumNetwork.IsHost)
        {
            ApplyGlobalSettingsFromHostConfig();
            BroadcastGlobalSettings();
            ActivateMode(NextEnabledMode(GameMode.None), true);
        }
    }

    private static void OnPlayerEntered(CSteamID player)
    {
        if (MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPCTarget(ModId, nameof(Plugin.SyncGlobalSettings), player,
                ReliableType.Reliable, EffectiveRespawnDelaySeconds);
            MyceliumNetwork.RPCTarget(ModId, nameof(Plugin.SyncActiveGameMode), player,
                ReliableType.Reliable, (int)ActiveMode, RoundId, (int)Phase);
        }
    }

    private static GameMode NextEnabledMode(GameMode current)
    {
        List<GameMode> modes = GetConfiguredModes();
        if (modes.Count == 0)
        {
            return GameMode.None;
        }

        int start = modes.IndexOf(current);
        return modes[(start + 1 + modes.Count) % modes.Count];
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
        return Modes.TryGetValue(mode, out ModeDescriptor? descriptor) && descriptor.IsEnabled();
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

    internal static void ApplyActiveMode(int mode, int roundId, int phase)
    {
        if (roundId < RoundId)
        {
            return;
        }

        GameMode nextMode = (GameMode)mode;
        if (ActiveMode != nextMode)
        {
            ResetMatchState();
            ActiveMode = nextMode;
        }

        RoundId = roundId;
        Phase = Enum.IsDefined(typeof(GameModePhase), phase)
            ? (GameModePhase)phase
            : GameModePhase.Inactive;
    }

    internal static void ResetMatchState()
    {
        _customRoundTransitionPending = false;
        PendingDeaths.Clear();
        foreach (ModeDescriptor descriptor in Modes.Values)
        {
            descriptor.Reset();
        }
    }

    internal static void ResetGameState()
    {
        ResetMatchState();
        RoundId++;
        Phase = ActiveMode == GameMode.None || !MyceliumNetwork.InLobby
            ? GameModePhase.Inactive
            : GameModePhase.Lobby;
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
            (int)ActiveMode, RoundId, (int)Phase);
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
        yield return new WaitForSeconds(4f);
        if (roundId == RoundId && Phase == GameModePhase.EndingRound && SceneMotor.Instance != null)
        {
            SceneMotor.Instance.ChangeNetworkScene();
        }
    }

    internal static bool HandleServerDeath(int playerId)
    {
        GameMode mode = ActiveMode;
        if (mode == GameMode.None || mode == GameMode.Default || Plugin.Instance == null)
        {
            return false;
        }

        if (PendingDeaths.Add(playerId))
        {
            Plugin.Instance.StartCoroutine(ProcessServerDeath(playerId, mode, RoundId));
        }
        return true;
    }

    private static IEnumerator ProcessServerDeath(int playerId, GameMode mode, int roundId)
    {
        // Gun's lethal-hit RPC calls PlayerDied before it writes PlayerHealth.killer.
        // Let that RPC finish before resolving the attacker.
        yield return null;
        PendingDeaths.Remove(playerId);

        if (ActiveMode != mode || RoundId != roundId || Phase == GameModePhase.EndingRound)
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
        }
    }
}

[HarmonyLib.HarmonyPatch(typeof(GameManager), "RpcLogic___PlayerDied_3316948804")]
internal static class GameManager_GameModeDeath_Patch
{
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
    public void SyncGlobalSettings(float respawnDelaySeconds)
    {
        GameModeManager.EffectiveRespawnDelaySeconds = respawnDelaySeconds;
    }

    [CustomRPC]
    public void SyncActiveGameMode(int mode, int roundId, int phase)
    {
        GameModeManager.ApplyActiveMode(mode, roundId, phase);
    }
}

[HarmonyLib.HarmonyPatch(typeof(GameManager), "ResetGame")]
internal static class GameManager_GameModeReset_Patch
{
    private static void Postfix()
    {
        GameModeManager.ResetGameState();
        JuggernautOutline.ResetState();
    }
}

[HarmonyLib.HarmonyPatch(typeof(PauseManager), "InvokeRoundStarted")]
internal static class PauseManager_GameModeLifecycle_Patch
{
    private static void Postfix()
    {
        GameModeManager.BeginRound();
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