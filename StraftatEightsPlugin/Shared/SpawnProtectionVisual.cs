using System.Collections.Generic;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class SpawnProtectionVisual
{
    private const float GhostOutlineThickness = 0.10f;

    private readonly struct OutlineSnapshot
    {
        internal readonly float Width;
        internal readonly Color Color;

        internal OutlineSnapshot(float width, Color color)
        {
            Width = width;
            Color = color;
        }
    }

    private static readonly Dictionary<SkinnedMeshRenderer, OutlineSnapshot> Snapshots = new();
    private static readonly HashSet<SkinnedMeshRenderer> ActiveRenderers = new();
    private static readonly List<SkinnedMeshRenderer> RenderersToRestore = new();
    private static readonly Color GhostColor = new(0.65f, 0.65f, 0.65f);

    internal static void Enforce()
    {
        ActiveRenderers.Clear();
        foreach (int playerId in SpawnProtectionState.ActivePlayerIds())
        {
            PlayerHealth? health = PlayerLookup.FindPlayerHealthById(playerId);
            if (health == null || !health || !health.gameObject.activeInHierarchy)
            {
                continue;
            }

            foreach (SkinnedMeshRenderer renderer in health.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Material[] materials = renderer.materials;
                if (materials.Length == 0 || materials[0] == null
                    || !materials[0].HasProperty("_ASEOutlineWidth"))
                {
                    continue;
                }

                ActiveRenderers.Add(renderer);
                if (!Snapshots.ContainsKey(renderer))
                {
                    Snapshots[renderer] = new OutlineSnapshot(
                        materials[0].GetFloat("_ASEOutlineWidth"),
                        materials[0].HasProperty("_ASEOutlineColor")
                            ? materials[0].GetColor("_ASEOutlineColor")
                            : Color.clear);
                }

                materials[0].SetFloat("_ASEOutlineWidth", GhostOutlineThickness);
                if (materials[0].HasProperty("_ASEOutlineColor"))
                {
                    materials[0].SetColor("_ASEOutlineColor", GhostColor);
                }
                renderer.materials = materials;
            }
        }

        RenderersToRestore.Clear();
        foreach (SkinnedMeshRenderer renderer in Snapshots.Keys)
        {
            if (renderer == null || !renderer || !ActiveRenderers.Contains(renderer))
            {
                RenderersToRestore.Add(renderer!);
            }
        }

        foreach (SkinnedMeshRenderer renderer in RenderersToRestore)
        {
            Restore(renderer);
        }
    }

    internal static void ResetState()
    {
        RenderersToRestore.Clear();
        foreach (SkinnedMeshRenderer renderer in Snapshots.Keys)
        {
            RenderersToRestore.Add(renderer);
        }

        foreach (SkinnedMeshRenderer renderer in RenderersToRestore)
        {
            Restore(renderer);
        }
        ActiveRenderers.Clear();
    }

    private static void Restore(SkinnedMeshRenderer renderer)
    {
        if (renderer != null && renderer && Snapshots.TryGetValue(renderer, out OutlineSnapshot snapshot))
        {
            Material[] materials = renderer.materials;
            if (materials.Length > 0 && materials[0] != null && materials[0].HasProperty("_ASEOutlineWidth"))
            {
                materials[0].SetFloat("_ASEOutlineWidth", snapshot.Width);
                if (materials[0].HasProperty("_ASEOutlineColor"))
                {
                    materials[0].SetColor("_ASEOutlineColor", snapshot.Color);
                }
                renderer.materials = materials;
            }
        }

        Snapshots.Remove(renderer!);
    }
}
