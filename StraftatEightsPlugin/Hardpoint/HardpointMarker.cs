using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class HardpointMarker
{
    private const int RingSegments = 64;
    private const float InnerRadiusRatio = 0.975f;
    private const float RingThicknessRatio = 1f - InnerRadiusRatio;
    private const float RingGroundClearance = 0.1f;
    private const float GroundProbeStartOffset = 4f;
    private const float GroundProbeDistance = 8f;
    private const float ActiveAlpha = 0.8f;
    private const int GeometryProbeSegments = 32;
    private const float GeometryProbeMargin = 0.02f;
    private static GameObject? _activeMarker;
    private static GameObject? _nextMarker;
    private static Renderer? _nextRenderer;
    private static bool _activePositionCached;
    private static Vector3 _activeObjectivePosition;
    private static Vector3 _activeMarkerPosition;
    private static bool _activeMeshConformed;
    private static Vector3 _activeMeshObjectivePosition;
    private static bool _nextPositionCached;
    private static Vector3 _nextObjectivePosition;
    private static Vector3 _nextMarkerPosition;
    private static bool _nextMeshConformed;
    private static Vector3 _nextMeshObjectivePosition;

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
        PositionMarker(_activeMarker, current, activeColor, false);

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
            PositionMarker(_nextMarker, next, new Color(1f, 0.75f, 0.1f, pulse), true);
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

    internal static void LogGeometryProbe()
    {
        if (!GameModeManager.IsActive(GameMode.Hardpoint)
            || !HardpointState.TryGetCurrentObjective(out HardpointObjective current))
        {
            Plugin.Logger.LogWarning("[HardpointMarker] Geometry probe unavailable: no active Hardpoint objective.");
            return;
        }

        ProbeGeometry("active", current, false);
        if (HardpointState.IsWarningActive
            && HardpointState.TryGetNextObjective(out HardpointObjective next))
        {
            ProbeGeometry("next", next, true);
        }
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
        Vector3[] vertices = new Vector3[RingSegments * 4];
        int[] triangles = new int[RingSegments * 24];
        for (int index = 0; index < RingSegments; index++)
        {
            float angle = index * Mathf.PI * 2f / RingSegments;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            int vertexOffset = index * 4;
            vertices[vertexOffset] = new Vector3(x, 0f, z);
            vertices[vertexOffset + 1] = new Vector3(x * InnerRadiusRatio, 0f,
                z * InnerRadiusRatio);
            vertices[vertexOffset + 2] = new Vector3(x, RingThicknessRatio, z);
            vertices[vertexOffset + 3] = new Vector3(x * InnerRadiusRatio,
                RingThicknessRatio, z * InnerRadiusRatio);

            int nextIndex = (index + 1) % RingSegments;
            int nextVertexOffset = nextIndex * 4;
            int triangleOffset = index * 24;

            triangles[triangleOffset] = vertexOffset + 2;
            triangles[triangleOffset + 1] = nextVertexOffset + 3;
            triangles[triangleOffset + 2] = nextVertexOffset + 2;
            triangles[triangleOffset + 3] = vertexOffset + 2;
            triangles[triangleOffset + 4] = vertexOffset + 3;
            triangles[triangleOffset + 5] = nextVertexOffset + 3;

            triangles[triangleOffset + 6] = vertexOffset;
            triangles[triangleOffset + 7] = nextVertexOffset;
            triangles[triangleOffset + 8] = nextVertexOffset + 1;
            triangles[triangleOffset + 9] = vertexOffset;
            triangles[triangleOffset + 10] = nextVertexOffset + 1;
            triangles[triangleOffset + 11] = vertexOffset + 1;

            triangles[triangleOffset + 12] = vertexOffset;
            triangles[triangleOffset + 13] = nextVertexOffset + 2;
            triangles[triangleOffset + 14] = nextVertexOffset;
            triangles[triangleOffset + 15] = vertexOffset;
            triangles[triangleOffset + 16] = vertexOffset + 2;
            triangles[triangleOffset + 17] = nextVertexOffset + 2;

            triangles[triangleOffset + 18] = vertexOffset + 1;
            triangles[triangleOffset + 19] = nextVertexOffset + 1;
            triangles[triangleOffset + 20] = nextVertexOffset + 3;
            triangles[triangleOffset + 21] = vertexOffset + 1;
            triangles[triangleOffset + 22] = nextVertexOffset + 3;
            triangles[triangleOffset + 23] = vertexOffset + 3;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void ProbeGeometry(string label, HardpointObjective objective, bool isNext)
    {
        float outerRadius = objective.Radius;
        float ringWidth = outerRadius * RingThicknessRatio;
        float centerRadius = outerRadius - ringWidth * 0.5f;
        float ringHeight = outerRadius * RingThicknessRatio;
        Vector3 ringBase = GetMarkerPosition(objective, isNext);
        Vector3 ringCenter = ringBase + Vector3.up * (ringHeight * 0.5f);
        float radialHalfExtent = ringWidth * 0.5f
            + centerRadius * (1f - Mathf.Cos(Mathf.PI / GeometryProbeSegments))
            + GeometryProbeMargin;
        float tangentialHalfExtent = centerRadius
            * Mathf.Sin(Mathf.PI / GeometryProbeSegments) + GeometryProbeMargin;
        float verticalHalfExtent = ringHeight * 0.5f + GeometryProbeMargin;
        HashSet<Collider> colliders = new();

        for (int index = 0; index < GeometryProbeSegments; index++)
        {
            float angle = index * Mathf.PI * 2f / GeometryProbeSegments;
            Vector3 center = ringBase + new Vector3(Mathf.Cos(angle) * centerRadius,
                ringHeight * 0.5f, Mathf.Sin(angle) * centerRadius);
            if (TryGetGroundY(center, objective.Position.y, out float sampledGroundY))
            {
                center.y = sampledGroundY + RingGroundClearance + ringHeight * 0.5f;
            }
            Quaternion rotation = Quaternion.AngleAxis(-angle * Mathf.Rad2Deg, Vector3.up);
            Collider[] overlaps = Physics.OverlapBox(center,
                new Vector3(radialHalfExtent, verticalHalfExtent, tangentialHalfExtent),
                rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            foreach (Collider collider in overlaps)
            {
                if (collider != null && collider.GetComponentInParent<PlayerHealth>() == null)
                {
                    colliders.Add(collider);
                }
            }
        }

        Plugin.Logger.LogInfo($"[HardpointMarker] Geometry probe {label}: "
            + $"intersectsMap={colliders.Count > 0} colliders={colliders.Count} "
            + $"center={ringCenter} height={ringHeight:0.###} width={ringWidth:0.###}");
        foreach (Collider collider in colliders)
        {
            Plugin.Logger.LogInfo($"[HardpointMarker] Geometry probe {label} hit: "
                + $"{collider.name} layer={collider.gameObject.layer}");
        }
    }

    private static void EnsureMeshConformsToGround(GameObject marker,
        HardpointObjective objective, bool isNext)
    {
        if (isNext)
        {
            if (_nextMeshConformed && _nextMeshObjectivePosition == objective.Position)
            {
                return;
            }

            _nextMeshObjectivePosition = objective.Position;
            _nextMeshConformed = true;
        }
        else
        {
            if (_activeMeshConformed && _activeMeshObjectivePosition == objective.Position)
            {
                return;
            }

            _activeMeshObjectivePosition = objective.Position;
            _activeMeshConformed = true;
        }

        MeshFilter? meshFilter = marker.GetComponent<MeshFilter>();
        Mesh? mesh = meshFilter?.sharedMesh;
        if (mesh == null)
        {
            return;
        }

        Vector3[] vertices = mesh.vertices;
        float ringHeight = objective.Radius * RingThicknessRatio;
        for (int index = 0; index < RingSegments; index++)
        {
            int vertexOffset = index * 4;
            SetGroundConformedVertex(vertices, vertexOffset, marker.transform,
                objective, ringHeight, false);
            SetGroundConformedVertex(vertices, vertexOffset + 1, marker.transform,
                objective, ringHeight, false);
            SetGroundConformedVertex(vertices, vertexOffset + 2, marker.transform,
                objective, ringHeight, true);
            SetGroundConformedVertex(vertices, vertexOffset + 3, marker.transform,
                objective, ringHeight, true);
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private static void SetGroundConformedVertex(Vector3[] vertices, int vertexIndex,
        Transform markerTransform, HardpointObjective objective, float ringHeight, bool top)
    {
        Vector3 vertex = vertices[vertexIndex];
        Vector3 worldPosition = markerTransform.position
            + new Vector3(vertex.x * objective.Radius, 0f, vertex.z * objective.Radius);
        float groundY = objective.Position.y;
        if (TryGetGroundY(worldPosition, objective.Position.y, out float sampledGroundY))
        {
            groundY = sampledGroundY;
        }

        float worldY = groundY + RingGroundClearance + (top ? ringHeight : 0f);
        vertex.y = (worldY - markerTransform.position.y) / objective.Radius;
        vertices[vertexIndex] = vertex;
    }

    private static Vector3 GetMarkerPosition(HardpointObjective objective, bool isNext)
    {
        if (isNext)
        {
            if (!_nextPositionCached || _nextObjectivePosition != objective.Position)
            {
                _nextObjectivePosition = objective.Position;
                _nextMarkerPosition = CalculateMarkerPosition(objective.Position);
                _nextPositionCached = true;
            }

            return _nextMarkerPosition;
        }

        if (!_activePositionCached || _activeObjectivePosition != objective.Position)
        {
            _activeObjectivePosition = objective.Position;
            _activeMarkerPosition = CalculateMarkerPosition(objective.Position);
            _activePositionCached = true;
        }

        return _activeMarkerPosition;
    }

    private static Vector3 CalculateMarkerPosition(Vector3 objectivePosition)
    {
        float groundY = objectivePosition.y;
        if (TryGetGroundY(objectivePosition, objectivePosition.y, out float sampledGroundY))
        {
            groundY = sampledGroundY;
        }

        return new Vector3(objectivePosition.x, groundY + RingGroundClearance,
            objectivePosition.z);
    }

    private static bool TryGetGroundY(Vector3 position, float referenceY, out float groundY)
    {
        groundY = referenceY;
        Vector3 rayOrigin = new(position.x, referenceY + GroundProbeStartOffset, position.z);
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, GroundProbeDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float closestGroundDifference = float.MaxValue;
        bool foundGround = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.GetComponentInParent<PlayerHealth>() != null)
            {
                continue;
            }

            float groundDifference = Mathf.Abs(hit.point.y - referenceY);
            if (groundDifference < closestGroundDifference)
            {
                closestGroundDifference = groundDifference;
                groundY = hit.point.y;
                foundGround = true;
            }
        }

        return foundGround;
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

    private static void PositionMarker(GameObject marker, HardpointObjective objective,
        Color color, bool isNext)
    {
        marker.SetActive(true);
        marker.transform.SetPositionAndRotation(GetMarkerPosition(objective, isNext),
            Quaternion.identity);
        marker.transform.localScale = new Vector3(objective.Radius, objective.Radius,
            objective.Radius);
        EnsureMeshConformsToGround(marker, objective, isNext);
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
            return new Color(1f, 1f, 1f, ActiveAlpha);
        }

        TeamColorData teamColor = TeamRules.GetColor(controller);
        return new Color(teamColor.Red / 255f, teamColor.Green / 255f,
            teamColor.Blue / 255f, ActiveAlpha);
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
        _activePositionCached = false;
        _activeMeshConformed = false;
        _nextPositionCached = false;
        _nextMeshConformed = false;
    }
}