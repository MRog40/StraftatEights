using UnityEngine;

namespace Eights;

internal sealed class HuntersVariantDefinition
{
    internal HuntersVariantDefinition(GameMode mode, string label, Color color, uint modId,
        string settingsLobbyDataKey, string liveLobbyDataKey, string settingsRpcName,
        string liveRpcName, string teamZeroName, string teamOneName,
        string teamZeroWeaponName, string teamOneWeaponName,
        bool teamZeroDualWield = false, float healthOverride = 0f,
        bool forceCrouch = false, bool disableHealthRegen = false)
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
        HealthOverride = healthOverride;
        ForceCrouch = forceCrouch;
        DisableHealthRegen = disableHealthRegen;
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
    internal float HealthOverride { get; }
    internal bool ForceCrouch { get; }
    internal bool DisableHealthRegen { get; }
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
        "RABBIT HUNT",
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

    internal static class TankBattleDefinition
    {
        internal static readonly HuntersVariantDefinition Value = new(
        GameMode.TankBattle,
        "TANK BATTLE",
        new Color32(190, 118, 52, 255),
        1618034001u,
        "Eights_TankBattle_Settings",
        "Eights_TankBattle_Live",
        "SyncTankBattleSettings",
        "SyncTankBattleLiveState",
        "TANKS A",
        "TANKS B",
        "HK_Caws",
        "HK_Caws",
        healthOverride: 400f,
        forceCrouch: true,
        disableHealthRegen: true);
}
