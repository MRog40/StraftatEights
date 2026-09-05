using MyceliumNetworking;
using Steamworks;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

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

    internal static void CycleForNextMap()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
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
        ActiveMode = (GameMode)mode;
        JuggernautState.ResetMatchState();
        FFAState.ResetMatchState();
        GunGameState.ResetMatchState();
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