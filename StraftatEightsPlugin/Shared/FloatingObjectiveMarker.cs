using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class FloatingObjectiveMarker
{
    private const int RingSegments = 64;
    private const float InnerRadiusRatio = 0.9875f;
    private const float RingThicknessRatio = 1f - InnerRadiusRatio;
    internal const float RingGroundClearance = 0f;
    private const float GroundProbeStartOffset = 4f;
    private const float GroundProbeDistance = 8f;
    internal const float OverheadMarkerHeight = 3f;
    private static readonly Dictionary<GameObject, Vector3> ConformedPositions = new();
    private static readonly Dictionary<Vector3, Vector3> GroundedPositionCache = new();
    private static readonly RaycastHit[] GroundProbeHits = new RaycastHit[32];
    private static int _groundedPositionCacheFrame = -1;

    internal static GameObject CreateRing(string name, Color color)
    {
        GameObject marker = new(name);
        MeshFilter meshFilter = marker.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = CreateRingMesh();
        MeshRenderer meshRenderer = marker.AddComponent<MeshRenderer>();
        Shader? shader = Shader.Find("Sprites/Default")
            ?? Shader.Find("Unlit/Transparent")
            ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
            ?? Shader.Find("Standard");
        if (shader != null)
        {
            Material material = new(shader);
            ConfigureTransparentMaterial(material);
            material.color = color;
            meshRenderer.material = material;
        }

        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        return marker;
    }

    internal static GameObject CreateOverheadDiamond(string name, Color color)
    {
        GameObject marker = new(name);
        MeshFilter meshFilter = marker.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = CreateOverheadMarkerMesh();
        MeshRenderer meshRenderer = marker.AddComponent<MeshRenderer>();
        Shader? shader = Shader.Find("Hidden/Internal-Colored")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            Material material = new(shader);
            ConfigureOverlayMaterial(material);
            material.color = color;
            meshRenderer.material = material;
        }

        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        return marker;
    }

    internal static GameObject CreateOverheadCircle(string name, Color color)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = name;
        Collider? collider = marker.GetComponent<Collider>();
        if (collider != null)
        {
            Object.Destroy(collider);
        }

        MeshRenderer meshRenderer = marker.GetComponent<MeshRenderer>()!;
        Shader? shader = Shader.Find("Hidden/Internal-Colored")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            Material material = new(shader);
            ConfigureOverlayMaterial(material);
            material.color = color;
            meshRenderer.material = material;
        }

        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        return marker;
    }

    internal static void PositionRing(GameObject marker, Vector3 objectivePosition,
        float radius, Color color)
    {
        marker.SetActive(true);
        marker.transform.SetPositionAndRotation(CalculateMarkerPosition(objectivePosition),
            Quaternion.identity);
        marker.transform.localScale = Vector3.one * radius;
        EnsureMeshConformsToGround(marker, objectivePosition, radius);
        SetRendererColor(marker, color);
    }

    internal static void PositionOverhead(GameObject marker, Vector3 objectivePosition,
        Color color, float size)
    {
        marker.SetActive(true);
        marker.transform.SetPositionAndRotation(
            CalculateMarkerPosition(objectivePosition)
                - Vector3.up * RingGroundClearance
                + Vector3.up * OverheadMarkerHeight,
            Quaternion.identity);
        marker.transform.localScale = Vector3.one * size;
        SetRendererColor(marker, color);
    }

    internal static Vector3 CalculateMarkerPosition(Vector3 objectivePosition)
    {
        if (_groundedPositionCacheFrame != Time.frameCount)
        {
            GroundedPositionCache.Clear();
            _groundedPositionCacheFrame = Time.frameCount;
        }

        if (GroundedPositionCache.TryGetValue(objectivePosition, out Vector3 cachedPosition))
        {
            return cachedPosition;
        }

        float groundY = objectivePosition.y;
        if (TryGetGroundY(objectivePosition, objectivePosition.y, out float sampledGroundY))
        {
            groundY = sampledGroundY;
        }

        Vector3 markerPosition = new(objectivePosition.x, groundY + RingGroundClearance,
            objectivePosition.z);
        GroundedPositionCache[objectivePosition] = markerPosition;
        return markerPosition;
    }

    internal static bool TryGetGroundY(Vector3 position, float referenceY, out float groundY)
    {
        groundY = referenceY;
        Vector3 rayOrigin = new(position.x, referenceY + GroundProbeStartOffset, position.z);
        int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, GroundProbeHits,
            GroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float closestGroundDifference = float.MaxValue;
        bool foundGround = false;
        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit hit = GroundProbeHits[index];
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

    internal static void Release(GameObject marker)
    {
        ConformedPositions.Remove(marker);
    }

    private static void EnsureMeshConformsToGround(GameObject marker,
        Vector3 objectivePosition, float radius)
    {
        if (ConformedPositions.TryGetValue(marker, out Vector3 previousPosition)
            && previousPosition == objectivePosition)
        {
            return;
        }

        ConformedPositions[marker] = objectivePosition;
        Mesh? mesh = marker.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null)
        {
            return;
        }

        Mesh meshValue = mesh;
        Vector3[] vertices = meshValue.vertices;
        float ringHeight = radius * RingThicknessRatio;
        for (int index = 0; index < RingSegments; index++)
        {
            int vertexOffset = index * 4;
            SetGroundConformedVertex(vertices, vertexOffset, marker.transform,
                objectivePosition, radius, ringHeight, false);
            SetGroundConformedVertex(vertices, vertexOffset + 1, marker.transform,
                objectivePosition, radius, ringHeight, false);
            SetGroundConformedVertex(vertices, vertexOffset + 2, marker.transform,
                objectivePosition, radius, ringHeight, true);
            SetGroundConformedVertex(vertices, vertexOffset + 3, marker.transform,
                objectivePosition, radius, ringHeight, true);
        }

        meshValue.vertices = vertices;
        meshValue.RecalculateNormals();
        meshValue.RecalculateBounds();
    }

    private static void SetGroundConformedVertex(Vector3[] vertices, int vertexIndex,
        Transform markerTransform, Vector3 objectivePosition, float radius, float ringHeight,
        bool top)
    {
        Vector3 vertex = vertices[vertexIndex];
        Vector3 worldPosition = markerTransform.position
            + new Vector3(vertex.x * radius, 0f, vertex.z * radius);
        float groundY = objectivePosition.y;
        if (TryGetGroundY(worldPosition, objectivePosition.y, out float sampledGroundY))
        {
            groundY = sampledGroundY;
        }

        float worldY = groundY + RingGroundClearance + (top ? ringHeight : 0f);
        vertex.y = (worldY - markerTransform.position.y) / radius;
        vertices[vertexIndex] = vertex;
    }

    private static void SetRendererColor(GameObject marker, Color color)
    {
        Renderer? renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = color;
        }
    }

    private static Mesh CreateRingMesh()
    {
        Mesh mesh = new() { name = "FloatingObjectiveRingMesh" };
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

    private static Mesh CreateOverheadMarkerMesh()
    {
        Mesh mesh = new()
        {
            name = "FloatingObjectiveOverheadMesh",
            vertices = new[]
            {
                new Vector3(0f, 1f, 0f), new Vector3(0f, -1f, 0f),
                new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f),
                new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, -1f)
            },
            triangles = new[]
            {
                0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 2,
                1, 3, 2, 1, 4, 3, 1, 5, 4, 1, 2, 5
            }
        };
        mesh.RecalculateNormals();
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

    private static void ConfigureOverlayMaterial(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", (int)CompareFunction.Always);
        material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        material.SetInt("_Cull", (int)CullMode.Off);
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Overlay;
    }
}