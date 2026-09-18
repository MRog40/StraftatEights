using System;
using System.IO;
using BepInEx.Bootstrap;
using ModMenu.Api;
using UnityEngine;

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
            Sprite? icon = LoadPluginIcon();
            if (icon != null)
            {
                ModMenuCustomisation.SetPluginIcon(icon);
            }

            ModMenuCustomisation.RegisterContentBuilder(BuildContent);
            DebugLog.Info("[ModMenu] Registered skip-round button.");
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogWarning("[ModMenu] Could not register custom settings: "
                + exception.GetBaseException().Message);
        }
    }

    private static Sprite? LoadPluginIcon()
    {
        string? pluginDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
        if (string.IsNullOrEmpty(pluginDirectory))
        {
            return null;
        }

        string iconPath = Path.Combine(pluginDirectory, "icon.png");
        if (!File.Exists(iconPath))
        {
            Plugin.Logger.LogWarning("[ModMenu] icon.png was not found beside the plugin DLL.");
            return null;
        }

        byte[] iconData = File.ReadAllBytes(iconPath);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, iconData, true))
        {
            UnityEngine.Object.Destroy(texture);
            Plugin.Logger.LogWarning("[ModMenu] Could not load icon.png.");
            return null;
        }

        return Sprite.Create(texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
    }

    private static void BuildContent(OptionListContext context)
    {
        context.InsertButton(0, "Global Settings", "Skip round",
            GameModeManager.SkipCurrentRound);
    }
}