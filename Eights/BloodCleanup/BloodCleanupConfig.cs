using BepInEx.Configuration;

namespace Eights;

public partial class Plugin
{
    internal static ConfigEntry<int> MaximumBloodEffects = null!;

    private void InitializeBloodCleanup()
    {
        MaximumBloodEffects = Config.Bind("Blood Settings", "Maximum Blood Effects", 50,
            new ConfigDescription(
                "Local visual limit for live blood effects. Older effects are removed first.",
                new AcceptableValueRange<int>(10, 100)));
        MaximumBloodEffects.SettingChanged += (_, _) => BloodCleanupState.TrimToLimit();
    }
}