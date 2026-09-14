using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class HardpointMarker
{
    private static GameObject? _activeMarker;
    private static GameObject? _nextMarker;
    private static Renderer? _nextRenderer;

    internal static void Update()
    {
        if (!GameModeManager.IsActive(GameMode.Hardpoint)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || !HardpointState.TryGetCurrentObjective(out HardpointObjective current))
        {
            Clear();
            return;
        }

        Color activeColor = GetActiveColor();
        _activeMarker ??= CreateMarker("HardpointActiveMarker", activeColor);
        PositionMarker(_activeMarker, current, activeColor);

        if (HardpointState.IsWarningActive
            && HardpointState.TryGetNextObjective(out HardpointObjective next))
        {
            _nextMarker ??= CreateMarker("HardpointNextMarker", new Color(1f, 0.75f, 0.1f, 0.08f));
            _nextRenderer ??= _nextMarker.GetComponent<Renderer>();
            float pulse = 0.04f + (Mathf.Sin(Time.unscaledTime * 7f) + 1f) * 0.03f;
            PositionMarker(_nextMarker, next, new Color(1f, 0.75f, 0.1f, pulse));
            if (_nextRenderer != null)
            {
                Color color = _nextRenderer.material.color;
                color.a = pulse;
                _nextRenderer.material.color = color;
            }
        }
        else if (_nextMarker != null)
        {
            _nextMarker.SetActive(false);
        }
    }

    internal static void ResetState()
    {
        Clear();
    }

    private static GameObject CreateMarker(string name, Color color)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = name;
        Collider? collider = marker.GetComponent<Collider>();
        if (collider != null)
        {
            Object.Destroy(collider);
        }

        Renderer renderer = marker.GetComponent<Renderer>();
        Shader? shader = Shader.Find("Unlit/Transparent")
            ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Standard");
        if (shader != null)
        {
            Material material = new(shader);
            if (shader.name == "Standard")
            {
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }

            material.color = color;
            material.renderQueue = 3000;
            renderer.material = material;
        }

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return marker;
    }

    private static void PositionMarker(GameObject marker, HardpointObjective objective, Color color)
    {
        marker.SetActive(true);
        marker.transform.SetPositionAndRotation(objective.Position, Quaternion.identity);
        marker.transform.localScale = new Vector3(objective.Radius * 2f, 1f,
            objective.Radius * 2f);
        Renderer? renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = color;
        }
    }

    private static Color GetActiveColor()
    {
        int controller = HardpointState.CurrentController;
        if (controller < 0)
        {
            return new Color(1f, 1f, 1f, 0.2f);
        }

        TeamColorData teamColor = TeamRules.GetColor(controller);
        return new Color(teamColor.Red / 255f, teamColor.Green / 255f,
            teamColor.Blue / 255f, 0.2f);
    }

    private static void Clear()
    {
        if (_activeMarker != null)
        {
            Object.Destroy(_activeMarker);
            _activeMarker = null;
        }

        if (_nextMarker != null)
        {
            Object.Destroy(_nextMarker);
            _nextMarker = null;
            _nextRenderer = null;
        }
    }
}