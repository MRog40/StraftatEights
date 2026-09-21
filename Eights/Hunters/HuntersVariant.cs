using UnityEngine;

namespace Eights;

internal sealed class HuntersVariantDefinition
{
    internal HuntersVariantDefinition(GameMode mode, string label, Color color, uint modId,
        string settingsLobbyDataKey, string liveLobbyDataKey, string settingsRpcName,
        string liveRpcName, string teamZeroName, string teamOneName,
        string teamZeroWeaponName, string teamOneWeaponName,
        bool teamZeroDualWield = false)
    {
        Mode = mode;
        Label = label;
        Color = color;
        ModId = modId;
        SettingsLobbyDataKey = settingsLobbyDataKey;
        LiveLobbyDataKey = liveLobbyDataKey;
        SettingsRpcName = settingsRpcName;
        LiveRpcName = liveRpcName;
        TeamZeroName = teamZeroName;
        TeamOneName = teamOneName;
        TeamZeroWeaponName = teamZeroWeaponName;
        TeamOneWeaponName = teamOneWeaponName;
        TeamZeroDualWield = teamZeroDualWield;
    }

    internal GameMode Mode { get; }
    internal string Label { get; }
    internal Color Color { get; }
    internal uint ModId { get; }
    internal string SettingsLobbyDataKey { get; }
    internal string LiveLobbyDataKey { get; }
    internal string SettingsRpcName { get; }
    internal string LiveRpcName { get; }
    internal string TeamZeroName { get; }
    internal string TeamOneName { get; }
    internal string TeamZeroWeaponName { get; }
    internal string TeamOneWeaponName { get; }
    internal bool TeamZeroDualWield { get; }
}

internal static class NinjaHuntersDefinition
{
    internal static readonly HuntersVariantDefinition Value = new(
        GameMode.NinjaHunters,
        "NINJA HUNTERS",
        new Color32(152, 91, 224, 255),
        1618033999u,
        "Eights_NinjaHunters_Settings",
        "Eights_NinjaHunters_Live",
        "SyncNinjaHuntersSettings",
        "SyncNinjaHuntersLiveState",
        "NINJAS",
        "HUNTERS",
        "Katana",
        "FG42");
}

internal static class RabbitHuntersDefinition
{
    internal static readonly HuntersVariantDefinition Value = new(
        GameMode.RabbitHunters,
        "RABBIT HUNTERS",
        new Color32(238, 156, 196, 255),
        1618034000u,
        "Eights_RabbitHunters_Settings",
        "Eights_RabbitHunters_Live",
        "SyncRabbitHuntersSettings",
        "SyncRabbitHuntersLiveState",
        "RABBITS",
        "HUNTERS",
        "Stylus",
        "Tromblonj",
        teamZeroDualWield: true);
}
