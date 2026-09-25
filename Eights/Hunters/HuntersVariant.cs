using UnityEngine;

namespace Eights;

internal sealed class HuntModeVariantDefinition
{
    internal HuntModeVariantDefinition(GameMode mode, string label, Color color, uint modId,
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

internal static class NinjatatDefinition
{
    internal static readonly HuntModeVariantDefinition Value = new(
        GameMode.Ninjatat,
        "NINJA HUNTERS",
        new Color32(152, 91, 224, 255),
        1618033999u,
        "Eights_Ninjatat_Settings",
        "Eights_Ninjatat_Live",
        "SyncNinjatatSettings",
        "SyncNinjatatLiveState",
        "NINJAS",
        "HUNTERS",
        "Katana",
        "FG42");
}

internal static class HunttatDefinition
{
    internal static readonly HuntModeVariantDefinition Value = new(
        GameMode.Hunttat,
        "Hunttat",
        new Color32(238, 156, 196, 255),
        1618034000u,
        "Eights_Hunttat_Settings",
        "Eights_Hunttat_Live",
        "SyncHunttatSettings",
        "SyncHunttatLiveState",
        "RABBITS",
        "HUNTERS",
        "SmithCarbine",
        "Tromblonj");
}

    internal static class TanktatDefinition
    {
        internal static readonly HuntModeVariantDefinition Value = new(
        GameMode.Tanktat,
        "TANK BATTLE",
        new Color32(190, 118, 52, 255),
        1618034001u,
        "Eights_Tanktat_Settings",
        "Eights_Tanktat_Live",
        "SyncTanktatSettings",
        "SyncTanktatLiveState",
        "TANKS A",
        "TANKS B",
        "HK_Caws",
        "HK_Caws",
        healthOverride: 400f,
        forceCrouch: true,
        disableHealthRegen: true);
}
