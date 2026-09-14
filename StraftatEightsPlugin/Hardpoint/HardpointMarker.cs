using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class HardpointMarker
{
    private const int RingSegments = 64;
    private const float InnerRadiusRatio = 0.97f;
    private const float RingVerticalOffset = 0.06f;
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
        if (_activeMarker == null || !_activeMarker)
        {
            _activeMarker = CreateMarker("HardpointActiveMarker", activeColor);
        }
        PositionMarker(_activeMarker, current, activeColor);

        if (HardpointState.IsWarningActive
            && HardpointState.TryGetNextObjective(out HardpointObjective next))
        {
            if (_nextMarker == null || !_nextMarker)
            {
                _nextMarker = CreateMarker("HardpointNextMarker",
                    new Color(1f, 0.75f, 0.1f, 0.08f));
                _nextRenderer = _nextMarker.GetComponent<Renderer>();
            }
            else if (_nextRenderer == null || !_nextRenderer)
            {
                _nextRenderer = _nextMarker.GetComponent<Renderer>();
            }

            float pulse = 0.04f + (Mathf.Sin(Time.unscaledTime * 7f) + 1f) * 0.03f;
            PositionMarker(_nextMarker, next, new Color(1f, 0.75f, 0.1f, pulse));
        }
        else if (_nextMarker != null && _nextMarker)
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
        GameObject marker = new(name);
        MeshFilter meshFilter = marker.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = CreateRingMesh();
        MeshRenderer meshRenderer = marker.AddComponent<MeshRenderer>();
        marker.name = name;
        Renderer renderer = meshRenderer;
        Shader? shader = Shader.Find("Sprites/Default")
            ?? Shader.Find("Unlit/Transparent")
            ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
            ?? Shader.Find("Standard");
        if (shader != null)
        {
            Material material = new(shader);
            ConfigureTransparentMaterial(material);
            material.color = color;
            renderer.material = material;
        }

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return marker;
    }

    private static Mesh CreateRingMesh()
    {
        Mesh mesh = new()
        {
            name = "HardpointRingMesh"
        };
        Vector3[] vertices = new Vector3[RingSegments * 2];
        int[] triangles = new int[RingSegments * 6];
        for (int index = 0; index < RingSegments; index++)
        {
            float angle = index * Mathf.PI * 2f / RingSegments;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            vertices[index * 2] = new Vector3(x, 0f, z);
            vertices[index * 2 + 1] = new Vector3(x * InnerRadiusRatio, 0f,
                z * InnerRadiusRatio);

            int nextIndex = (index + 1) % RingSegments;
            int triangleOffset = index * 6;
            triangles[triangleOffset] = index * 2;
            triangles[triangleOffset + 1] = nextIndex * 2 + 1;
            triangles[triangleOffset + 2] = nextIndex * 2;
            triangles[triangleOffset + 3] = index * 2;
            triangles[triangleOffset + 4] = index * 2 + 1;
            triangles[triangleOffset + 5] = nextIndex * 2 + 1;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_Cull", (int)CullMode.Off);
        material.SetFloat("_Mode", 3f);
        material.SetFloat("_Surface", 1f);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
    }

    private static void PositionMarker(GameObject marker, HardpointObjective objective, Color color)
    {
        marker.SetActive(true);
        marker.transform.SetPositionAndRotation(objective.Position
            + Vector3.up * RingVerticalOffset, Quaternion.identity);
        marker.transform.localScale = new Vector3(objective.Radius, 1f, objective.Radius);
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
        if (_activeMarker != null && _activeMarker)
        {
            Object.Destroy(_activeMarker);
        }
        _activeMarker = null;

        if (_nextMarker != null && _nextMarker)
        {
            Object.Destroy(_nextMarker);
        }
        _nextMarker = null;
        _nextRenderer = null;
    }
}