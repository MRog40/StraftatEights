using System;
using System.IO;
using BepInEx.Bootstrap;
using ModMenu.Api;
using UnityEngine;

namespace Eights;

internal static class ModMenuIntegration
{
    private const string ModMenuGuid = "kestrel.straftat.modmenu";

    internal static void Initialize()
    {
        if (!Chainloader.PluginInfos.ContainsKey(ModMenuGuid))
        {
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
        const string sectionSuffix = "/Global Settings";
        string? globalHeaderName = null;
        int insertionPosition = -1;
        int activePosition = 0;

        for (int childIndex = 0; childIndex < context.Root.childCount; childIndex++)
        {
            Transform child = context.Root.GetChild(childIndex);
            if (!child.gameObject.activeSelf)
            {
                continue;
            }

            if (globalHeaderName == null
                && child.name.EndsWith(sectionSuffix, StringComparison.Ordinal))
            {
                globalHeaderName = child.name;
                insertionPosition = activePosition + 1;
                break;
            }

            activePosition++;
        }

        if (globalHeaderName == null)
        {
            return;
        }

        if (insertionPosition >= 0)
        {
            context.InsertButton(insertionPosition, string.Empty, "Skip round",
                GameModeManager.SkipCurrentRound);
        }
        else
        {
            context.AppendButton(string.Empty, "Skip round", GameModeManager.SkipCurrentRound);
        }
    }
}