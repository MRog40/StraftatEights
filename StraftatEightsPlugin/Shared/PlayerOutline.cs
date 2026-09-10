using System.Collections.Generic;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class PlayerOutline
{
    private static readonly List<SkinnedMeshRenderer> AppliedRenderers = new();
    private static GameMode _lastMode = GameMode.None;

    internal static void ResetState()
    {
        ClearApplied();
        _lastMode = GameMode.None;
    }

    internal static bool UpdateMode(GameMode activeMode)
    {
        if (_lastMode == activeMode)
        {
            return false;
        }

        ClearAll();
        _lastMode = activeMode;
        return true;
    }

    internal static void ApplySingleTarget(ref PlayerHealth? current, PlayerHealth? target, Color color)
    {
        if (target == null || !target || !target.gameObject.activeInHierarchy)
        {
            ClearTarget(ref current);
            return;
        }

        if (current != target)
        {
            ClearApplied();
            current = target;
        }

        Apply(target, color);
    }

    internal static void ClearTarget(ref PlayerHealth? current)
    {
        if (current != null && current)
        {
            ClearApplied();
        }

        current = null;
    }

    internal static void Apply(PlayerHealth player, Color color)
    {
        if (player == null)
        {
            return;
        }

        foreach (SkinnedMeshRenderer renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Material[] materials = renderer.materials;
            if (materials.Length == 0 || materials[0] == null || !materials[0].HasProperty("_ASEOutlineWidth"))
            {
                continue;
            }

            float outlineWidth = renderer.gameObject.name == "SM_Aboubi_Head00" ? 0.02f : 0.04f;
            if (!Mathf.Approximately(materials[0].GetFloat("_ASEOutlineWidth"), outlineWidth)
                || materials[0].GetColor("_ASEOutlineColor") != color)
            {
                materials[0].SetFloat("_ASEOutlineWidth", outlineWidth);
                materials[0].SetColor("_ASEOutlineColor", color);
                renderer.materials = materials;
            }

            if (!AppliedRenderers.Contains(renderer))
            {
                AppliedRenderers.Add(renderer);
            }
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

    internal static void ApplyTemporary(PlayerHealth player, Color color, float outlineWidth)
    {
        if (player == null)
        {
            return;
        }

        foreach (SkinnedMeshRenderer renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Material[] materials = renderer.materials;
            if (materials.Length == 0 || materials[0] == null || !materials[0].HasProperty("_ASEOutlineWidth"))
            {
                continue;
            }

            if (!Mathf.Approximately(materials[0].GetFloat("_ASEOutlineWidth"), outlineWidth)
                || materials[0].GetColor("_ASEOutlineColor") != color)
            {
                materials[0].SetFloat("_ASEOutlineWidth", outlineWidth);
                materials[0].SetColor("_ASEOutlineColor", color);
                renderer.materials = materials;
            }
        }
    }

    internal static void ClearTemporary(PlayerHealth player)
    {
        Clear(player);
    }

    internal static void ClearAll()
    {
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client)
            {
                continue;
            }

            PlayerManager? playerManager = client.PlayerSpawner;
            if (playerManager == null || !playerManager || playerManager.player == null || !playerManager.player)
            {
                continue;
            }

            PlayerHealth? player = playerManager.player.GetComponent<PlayerHealth>();
            if (player != null && player)
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

    internal static bool HasAppliedRenderers => AppliedRenderers.Count > 0;

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