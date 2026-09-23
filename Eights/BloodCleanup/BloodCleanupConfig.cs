using BepInEx.Configuration;

namespace Eights;

public partial class Plugin
{
    internal static ConfigEntry<int> MaximumBloodEffects = null!;

    private void InitializeBloodCleanup()
    {
        MaximumBloodEffects = Config.Bind("Global Settings", "Maximum Blood Effects", 25,
            new ConfigDescription(
                "Local visual limit for live blood effects. Older effects are removed first.",
            new AcceptableValueRange<int>(0, 50)));
        MaximumBloodEffects.SettingChanged += (_, _) => BloodCleanupState.TrimToLimit();
    }
}