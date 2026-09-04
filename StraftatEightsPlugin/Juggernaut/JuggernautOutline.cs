using System.Collections.Generic;
using UnityEngine;

namespace StraftatEightsPlugin;

// Gives the current Juggernaut a colored outline visible to other players, using the same outline
// shader properties the base game's enemy-outline feature uses (_ASEOutlineWidth/_ASEOutlineColor on
// each body part's SkinnedMeshRenderer material).
internal static class JuggernautOutline
{
    private const string OutlineColorHex = "#FF6A00";

    private static int _outlinedPlayerId = -1;
    private static int _outlinedInstanceId = -1;
    private static readonly List<SkinnedMeshRenderer> OutlinedRenderers = new();

    internal static void ResetState()
    {
        ClearOutline();
        _outlinedPlayerId = -1;
        _outlinedInstanceId = -1;
    }

    internal static void EnforceOutline()
    {
        int targetId = GameModeManager.IsActive(GameMode.Juggernaut) && JuggernautState.ShowOutline ? JuggernautState.CurrentJuggernautPlayerId : -1;
        if (targetId != _outlinedPlayerId)
        {
            ClearOutline();
            _outlinedPlayerId = targetId;
            _outlinedInstanceId = -1;
        }

        if (targetId < 0)
        {
            return;
        }
        // No point outlining our own local player's model - it's not visible from first person anyway
        if (ClientInstance.Instance != null && ClientInstance.Instance.PlayerId == targetId)
        {
            return;
        }

        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(targetId);
        if (health == null || !health.gameObject.activeInHierarchy)
        {
            return;
        }

        int instanceId = health.GetInstanceID();
        if (instanceId == _outlinedInstanceId)
        {
            return;
        }
        ClearOutline();
        _outlinedInstanceId = instanceId;

        PlayerSetup setup = health.GetComponent<PlayerSetup>();
        if (setup == null || setup.meshesToChange == null)
        {
            return;
        }

        Color color = ColorFromHex(OutlineColorHex);
        foreach (GameObject meshObj in setup.meshesToChange)
        {
            if (meshObj == null)
            {
                continue;
            }
            SkinnedMeshRenderer renderer = meshObj.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null)
            {
                continue;
            }
            Material[] materials = renderer.materials;
            if (materials == null || materials.Length == 0 || materials[0] == null || !materials[0].HasProperty("_ASEOutlineWidth"))
            {
                continue;
            }

            materials[0].SetFloat("_ASEOutlineWidth", meshObj.name == "SM_Aboubi_Head00" ? 0.02f : 0.04f);
            materials[0].SetColor("_ASEOutlineColor", color);
            renderer.materials = materials;
            OutlinedRenderers.Add(renderer);
        }
    }

    private static void ClearOutline()
    {
        foreach (SkinnedMeshRenderer renderer in OutlinedRenderers)
        {
            if (renderer == null)
            {
                continue;
            }
            Material[] materials = renderer.materials;
            if (materials == null || materials.Length == 0 || materials[0] == null || !materials[0].HasProperty("_ASEOutlineWidth"))
            {
                continue;
            }
            materials[0].SetFloat("_ASEOutlineWidth", 0f);
            renderer.materials = materials;
        }
        OutlinedRenderers.Clear();
    }

    private static Color ColorFromHex(string hex)
    {
        return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.white;
    }
}
