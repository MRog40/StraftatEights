using System.Collections;
using MyceliumNetworking;
using Steamworks;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StraftatEightsPlugin;

internal enum GameMode
{
    None = 0,
    FreeForAll = 1,
    Juggernaut = 2,
    GunGame = 3
}

internal static class GameModeManager
{
    internal const uint ModId = 1618033988u;
    internal static GameMode ActiveMode { get; private set; }
    internal static ConfigEntry<string> ModeOrder = null!;
    internal static ConfigEntry<bool> RandomModes = null!;

    internal static void Initialize()
    {
        ModeOrder = Plugin.Instance.Config.Bind("Mode Manager Settings", "Mode Order", "1, 2, 3",
            "Host-controlled: comma-separated game mode IDs. FFA is 1, Juggernaut is 2, and Gun Game is 3.");
        RandomModes = Plugin.Instance.Config.Bind("Mode Manager Settings", "Random Game Modes", false,
            "Host-controlled: choose the next enabled game mode at random instead of following Mode Order.");
        ModeOrder.SettingChanged += (_, _) => OnSettingsChanged();
        RandomModes.SettingChanged += (_, _) => OnSettingsChanged();

        MyceliumNetwork.RegisterNetworkObject(Plugin.Instance, ModId);
        MyceliumNetwork.LobbyCreated += OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += OnPlayerEntered;
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
        SetActiveMode(NextEnabledMode(ActiveMode));
    }

    internal static bool IsActive(GameMode mode)
    {
        return ActiveMode == mode;
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
            SetActiveMode(NextEnabledMode(GameMode.None));
        }
    }

    private static void OnPlayerEntered(CSteamID player)
    {
        if (MyceliumNetwork.IsHost)
        {
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
        if (RandomModes.Value)
        {
            return modes[UnityEngine.Random.Range(0, modes.Count)];
        }

        int start = modes.IndexOf(current);
        return modes[(start + 1 + modes.Count) % modes.Count];
    }

    private static List<GameMode> GetConfiguredModes()
    {
        List<GameMode> modes = new();
        foreach (string value in ModeOrder.Value.Split(',', ';'))
        {
            GameMode mode = ParseMode(value);
            if (mode != GameMode.None && IsEnabled(mode) && !modes.Contains(mode))
            {
                modes.Add(mode);
            }
        }

        GameMode[] allModes = { GameMode.FreeForAll, GameMode.Juggernaut, GameMode.GunGame };
        foreach (GameMode mode in allModes)
        {
            if (IsEnabled(mode) && !modes.Contains(mode))
            {
                modes.Add(mode);
            }
        }
        return modes;
    }

    private static GameMode ParseMode(string value)
    {
        string normalized = string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToLowerInvariant();
        return normalized switch
        {
            "1" or "ffa" or "freeforall" => GameMode.FreeForAll,
            "2" or "juggernaut" => GameMode.Juggernaut,
            "3" or "gungame" => GameMode.GunGame,
            _ => GameMode.None
        };
    }

    private static bool IsEnabled(GameMode mode)
    {
        return mode switch
        {
            GameMode.Juggernaut => Plugin.JuggernautEnabled.Value,
            GameMode.FreeForAll => Plugin.FFAEnabled.Value,
            GameMode.GunGame => Plugin.GunGameEnabled.Value,
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
        if (mode == GameMode.None || Plugin.Instance == null)
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
                GameModeRespawn.Schedule(playerId, FFAState.RespawnDelaySeconds);
                break;
            case GameMode.Juggernaut:
                JuggernautState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, JuggernautState.RespawnDelaySeconds);
                break;
            case GameMode.GunGame:
                GunGameState.OnServerKill(playerId, killerId);
                GameModeRespawn.Schedule(playerId, GunGameState.RespawnDelaySeconds);
                break;
        }
    }
}

[HarmonyLib.HarmonyPatch(typeof(GameManager), "RpcLogic___PlayerDied_3316948804")]
internal static class GameManager_GameModeDeath_Patch
{
    private static bool Prefix(GameManager __instance, int playerId)
    {
        if (!__instance.IsServer || GameModeManager.ActiveMode == GameMode.None)
        {
            return true;
        }

        return !GameModeManager.HandleServerDeath(playerId);
    }
}

public partial class Plugin
{
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