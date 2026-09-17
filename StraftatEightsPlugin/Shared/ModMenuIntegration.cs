using System;
using BepInEx.Bootstrap;
using ModMenu.Api;

namespace StraftatEightsPlugin;

internal static class ModMenuIntegration
{
    private const string ModMenuGuid = "kestrel.straftat.modmenu";

    internal static void Initialize()
    {
        if (!Chainloader.PluginInfos.ContainsKey(ModMenuGuid))
        {
            DebugLog.Info("[ModMenu] Not installed; skip-round button is unavailable.");
            return;
        }

        try
        {
            ModMenuCustomisation.RegisterContentBuilder(BuildContent);
            DebugLog.Info("[ModMenu] Registered skip-round button.");
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogWarning("[ModMenu] Could not register custom settings: "
                + exception.GetBaseException().Message);
        }
    }

    private static void BuildContent(OptionListContext context)
    {
        context.PrependButton("Round control", "Skip round", GameModeManager.SkipCurrentRound);
    }
}