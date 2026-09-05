using System.Collections.Generic;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class PlayerOutline
{
    private static readonly List<SkinnedMeshRenderer> AppliedRenderers = new();

    internal static void Apply(PlayerHealth player, Color color)
    {
        if (player == null)
        {
            return;
        }

        PlayerSetup? setup = player.GetComponent<PlayerSetup>();
        if (setup == null || setup.meshesToChange == null)
        {
            return;
        }

        foreach (GameObject meshObject in setup.meshesToChange)
        {
            if (meshObject == null)
            {
                continue;
            }

            SkinnedMeshRenderer? renderer = meshObject.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null)
            {
                continue;
            }

            Material[] materials = renderer.materials;
            if (materials.Length == 0 || materials[0] == null || !materials[0].HasProperty("_ASEOutlineWidth"))
            {
                continue;
            }

            materials[0].SetFloat("_ASEOutlineWidth", meshObject.name == "SM_Aboubi_Head00" ? 0.02f : 0.04f);
            materials[0].SetColor("_ASEOutlineColor", color);
            renderer.materials = materials;
            AppliedRenderers.Add(renderer);
        }
    }

    internal static void Clear(PlayerHealth player)
    {
        if (player == null)
        {
            return;
        }

        foreach (SkinnedMeshRenderer renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            ClearRenderer(renderer.gameObject);
        }
    }

    internal static void ClearAll()
    {
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            PlayerHealth? player = client?.PlayerSpawner?.player?.GetComponent<PlayerHealth>();
            if (player != null)
            {
                Clear(player);
            }
        }

        foreach (SkinnedMeshRenderer renderer in AppliedRenderers)
        {
            if (renderer != null)
            {
                ClearRenderer(renderer.gameObject);
            }
        }
        AppliedRenderers.Clear();
    }

    internal static void ClearApplied()
    {
        foreach (SkinnedMeshRenderer renderer in AppliedRenderers)
        {
            if (renderer != null)
            {
                ClearRenderer(renderer.gameObject);
            }
        }
        AppliedRenderers.Clear();
    }

    private static void ClearRenderer(GameObject meshObject)
    {
        if (meshObject == null)
        {
            return;
        }

        SkinnedMeshRenderer? renderer = meshObject.GetComponent<SkinnedMeshRenderer>();
        if (renderer == null)
        {
            return;
        }

        Material[] materials = renderer.materials;
        if (materials.Length == 0 || materials[0] == null || !materials[0].HasProperty("_ASEOutlineWidth"))
        {
            return;
        }

        materials[0].SetFloat("_ASEOutlineWidth", 0f);
        renderer.materials = materials;
    }
}