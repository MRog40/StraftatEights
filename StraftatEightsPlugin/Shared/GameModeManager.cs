using System.Collections;
using MyceliumNetworking;
using Steamworks;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using UnityEngine;

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

internal static class GameModeManager
{
    internal const uint ModId = 1618033988u;
    private static readonly Dictionary<GameMode, (string Label, Color Color)> DisplayInfo = new()
    {
        [GameMode.FreeForAll] = ("FFA", new Color32(85, 204, 255, 255)),
        [GameMode.Juggernaut] = ("JUGGERNAUT", new Color32(255, 106, 0, 255)),
        [GameMode.GunGame] = ("GUN GAME", new Color32(255, 221, 85, 255)),
        [GameMode.SniperBattle] = ("SNIPER BATTLE", new Color32(255, 96, 128, 255)),
        [GameMode.Default] = ("DEFAULT", new Color32(220, 220, 220, 255)),
        [GameMode.MichaelMeyers] = ("MICHAEL MEYERS", new Color32(204, 34, 34, 255))
    };

    internal static GameMode ActiveMode { get; private set; }
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
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
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
        MyceliumNetwork.RPC(ModId, nameof(Plugin.SyncActiveGameMode), ReliableType.Reliable, (int)ActiveMode);
    }

    internal static void CycleForNextMap()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        _customRoundTransitionPending = false;
        JuggernautState.ResetMatchState();
        FFAState.ResetMatchState();
        GunGameState.ResetMatchState();
        SniperBattleState.ResetMatchState();
        MichaelMeyersState.ResetMatchState();
        SetActiveMode(NextEnabledMode(ActiveMode));
    }

    internal static bool IsActive(GameMode mode)
    {
        return ActiveMode == mode;
    }

    internal static bool ShouldIgnoreGlobalWeaponSettings =>
        ActiveMode == GameMode.GunGame || ActiveMode == GameMode.SniperBattle || ActiveMode == GameMode.Default
        || ActiveMode == GameMode.MichaelMeyers;

    internal static bool ShouldIgnoreGlobalHealthSettings => ActiveMode == GameMode.SniperBattle || ActiveMode == GameMode.Default;

    internal static bool IsCustomMode => ActiveMode != GameMode.None && ActiveMode != GameMode.Default;

    internal static bool ShouldIgnoreGlobalWeaponSettingsFor(Weapon weapon)
    {
        return ShouldIgnoreGlobalWeaponSettings ||
            (ActiveMode == GameMode.Juggernaut && JuggernautState.IsCurrentJuggernautWeapon(weapon));
    }

    internal static string GetModeLabel(GameMode mode)
    {
        return DisplayInfo.TryGetValue(mode, out (string Label, Color Color) info) ? info.Label : "UNKNOWN";
    }

    internal static string GetModeLabelMarkup(GameMode mode)
    {
        if (!DisplayInfo.TryGetValue(mode, out (string Label, Color Color) info))
        {
            return "<b>UNKNOWN</b>";
        }

        return $"<b><color=#{ColorUtility.ToHtmlStringRGB(info.Color)}>{info.Label}</color></b>";
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
            SetActiveMode(NextEnabledMode(GameMode.None));
        }
    }

    private static void OnPlayerEntered(CSteamID player)
    {
        if (MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPCTarget(ModId, nameof(Plugin.SyncGlobalSettings), player,
                ReliableType.Reliable, EffectiveRespawnDelaySeconds);
            MyceliumNetwork.RPCTarget(ModId, nameof(Plugin.SyncActiveGameMode), player,
                ReliableType.Reliable, (int)ActiveMode);
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
        GameMode[] allModes = { GameMode.Default, GameMode.FreeForAll, GameMode.Juggernaut, GameMode.GunGame, GameMode.SniperBattle, GameMode.MichaelMeyers };
        foreach (GameMode mode in allModes)
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
        return mode switch
        {
            GameMode.Juggernaut => Plugin.JuggernautEnabled.Value,
            GameMode.FreeForAll => Plugin.FFAEnabled.Value,
            GameMode.GunGame => Plugin.GunGameEnabled.Value,
            GameMode.SniperBattle => Plugin.SniperBattleEnabled.Value,
            GameMode.Default => Plugin.DefaultGameModeEnabled.Value,
            GameMode.MichaelMeyers => Plugin.MichaelMeyersEnabled.Value,
            _ => false
        };
    }

    private static void SetActiveMode(GameMode mode)
    {
        if (ActiveMode == mode)
        {
            return;
        }
        ActiveMode = mode;
        JuggernautState.ResetMatchState();
        FFAState.ResetMatchState();
        GunGameState.ResetMatchState();
        SniperBattleState.ResetMatchState();
        MichaelMeyersState.ResetMatchState();
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(ModId, nameof(Plugin.SyncActiveGameMode), ReliableType.Reliable, (int)mode);
        }
    }

    internal static void ApplyActiveMode(int mode)
    {
        GameMode nextMode = (GameMode)mode;
        if (ActiveMode == nextMode)
        {
            return;
        }

        ActiveMode = nextMode;
        JuggernautState.ResetMatchState();
        FFAState.ResetMatchState();
        GunGameState.ResetMatchState();
        SniperBattleState.ResetMatchState();
        MichaelMeyersState.ResetMatchState();
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

        ScoreManager.Instance.ResetRound();
        ScoreManager.Instance.AddPoints(winningTeamId);
        RoundManager.Instance.CmdEndRound(winningTeamId);
        Plugin.Instance.StartCoroutine(AdvanceAfterCustomRound());
    }

    private static IEnumerator AdvanceAfterCustomRound()
    {
        yield return new WaitForSeconds(4f);
        if (SceneMotor.Instance != null)
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
            Plugin.Instance.StartCoroutine(ProcessServerDeath(playerId, mode));
        }
        return true;
    }

    private static IEnumerator ProcessServerDeath(int playerId, GameMode mode)
    {
        // Gun's lethal-hit RPC calls PlayerDied before it writes PlayerHealth.killer.
        // Let that RPC finish before resolving the attacker.
        yield return null;
        PendingDeaths.Remove(playerId);

        if (ActiveMode != mode)
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
    public void SyncActiveGameMode(int mode)
    {
        GameModeManager.ApplyActiveMode(mode);
    }
}

[HarmonyLib.HarmonyPatch(typeof(SceneMotor), "ChangeNetworkScene")]
internal static class SceneMotor_GameModeCycle_Patch
{
    private static void Prefix()
    {
        GameModeManager.CycleForNextMap();
    }
}