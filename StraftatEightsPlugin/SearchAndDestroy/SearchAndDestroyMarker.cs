using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyMarker
{
    private const float SiteMarkerHeight = 3f;
    private const float SiteMarkerSize = 0.8f;
    private const float SiteSquareSize = 4f;
    private const float SiteSquareThickness = 0.1f;
    private const float SiteSquareHeight = 0.05f;
    private const float CircleRadius = 0.55f;
    private const float CircleHeight = 0.05f;
    private const float GroundClearance = 0.1f;
    private const float BombMarkerHeight = 2.8f;
    private const float BombMarkerSize = 0.6f;
    private const float BombGroundMarkerHeight = 0.1f;
    private const int RingSegments = 48;
    private static readonly GameObject?[] SiteMarkers = new GameObject?[2];
    private static readonly bool[] SiteSquaresConformed = new bool[2];
    private static readonly Vector3[] SiteSquarePositions = new Vector3[2];
    private static GameObject? _bomb;

    internal static void Update()
    {
        if (!GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || GameModeManager.Phase != GameModePhase.ActiveRound)
        {
            Clear();
            return;
        }

        for (int siteIndex = 0; siteIndex < 2; siteIndex++)
        {
            if (!SearchAndDestroyState.TryGetSitePosition(siteIndex, out Vector3 position))
            {
                SetActive(SiteMarkers[siteIndex], false);
                continue;
            }

            if (SiteMarkers[siteIndex] == null || !SiteMarkers[siteIndex]!)
            {
                SiteMarkers[siteIndex] = CreateSiteMarker(siteIndex);
            }

            SiteMarkers[siteIndex]!.transform.SetPositionAndRotation(
                GetSiteMarkerPosition(position), Quaternion.identity);
            ConformSiteSquareToGround(SiteMarkers[siteIndex]!, siteIndex, position);
            SiteMarkers[siteIndex]!.SetActive(true);
        }

        UpdateBomb();
    }

    internal static void ResetState()
    {
        Clear();
    }

    private static GameObject CreateSiteMarker(int siteIndex)
    {
        GameObject root = new($"SearchAndDestroySite_{siteIndex}");
        root.transform.position = Vector3.zero;

        GameObject square = new("BombSiteSquare");
        square.transform.SetParent(root.transform, false);
        square.transform.localPosition = Vector3.up * 0.06f;
        MeshFilter squareFilter = square.AddComponent<MeshFilter>();
        squareFilter.sharedMesh = CreateSquareMesh();
        MeshRenderer squareRenderer = square.AddComponent<MeshRenderer>();
        ConfigureRenderer(squareRenderer, false);
        squareRenderer.material.color = new Color32(245, 245, 245, 190);

        GameObject marker = new(siteIndex == 0 ? "CircleMarker" : "DiamondMarker");
        marker.transform.SetParent(root.transform, false);
        marker.transform.localPosition = Vector3.up * SiteMarkerHeight;
        marker.transform.localScale = Vector3.one * SiteMarkerSize;
        MeshFilter markerFilter = marker.AddComponent<MeshFilter>();
        markerFilter.sharedMesh = siteIndex == 0 ? CreateCircleMesh() : CreateDiamondMesh();
        MeshRenderer markerRenderer = marker.AddComponent<MeshRenderer>();
        ConfigureRenderer(markerRenderer, true);
        markerRenderer.material.color = siteIndex == 0
            ? new Color32(220, 45, 45, 235)
            : new Color32(45, 110, 235, 235);

        return root;
    }

    private static void UpdateBomb()
    {
        if (SearchAndDestroyState.BombStatus == SearchAndDestroyBombStatus.Home
            || !SearchAndDestroyState.TryGetBombPosition(out Vector3 position))
        {
            SetActive(_bomb, false);
            return;
        }

        if (_bomb == null || !_bomb)
        {
            _bomb = new GameObject("SearchAndDestroyBomb");
            _bomb.name = "SearchAndDestroyBomb";
            MeshFilter filter = _bomb.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateDiamondMesh();
            MeshRenderer renderer = _bomb.AddComponent<MeshRenderer>();
            ConfigureRenderer(renderer, true);
            renderer.material.color = new Color32(35, 35, 35, 255);
        }

        bool carried = SearchAndDestroyState.BombStatus == SearchAndDestroyBombStatus.Carried;
        float markerHeight = carried ? BombMarkerHeight : BombGroundMarkerHeight;
        _bomb.transform.SetPositionAndRotation(position + Vector3.up * markerHeight,
            Quaternion.identity);
        _bomb.transform.localScale = Vector3.one * (carried ? BombMarkerSize : 0.8f);
        _bomb.SetActive(true);
    }

    private static Mesh CreateDiamondMesh()
    {
        Mesh mesh = new()
        {
            name = "SearchAndDestroyDiamondMesh",
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

    private static Mesh CreateCircleMesh()
    {
        Mesh mesh = new() { name = "SearchAndDestroyCircleMesh" };
        Vector3[] vertices = new Vector3[RingSegments * 4];
        int[] triangles = new int[RingSegments * 18];
        for (int index = 0; index < RingSegments; index++)
        {
            float angle = index * Mathf.PI * 2f / RingSegments;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            int vertex = index * 4;
            vertices[vertex] = new Vector3(x * CircleRadius, 0f, z * CircleRadius);
            vertices[vertex + 1] = new Vector3(x * CircleRadius, CircleHeight, z * CircleRadius);
            vertices[vertex + 2] = new Vector3(0f, 0f, 0f);
            vertices[vertex + 3] = new Vector3(0f, CircleHeight, 0f);

            int next = ((index + 1) % RingSegments) * 4;
            int triangle = index * 18;
            triangles[triangle] = vertex + 3;
            triangles[triangle + 1] = next + 1;
            triangles[triangle + 2] = vertex + 1;
            triangles[triangle + 3] = vertex + 2;
            triangles[triangle + 4] = vertex;
            triangles[triangle + 5] = next;
            triangles[triangle + 6] = vertex;
            triangles[triangle + 7] = next;
            triangles[triangle + 8] = next + 1;
            triangles[triangle + 9] = vertex;
            triangles[triangle + 10] = next + 1;
            triangles[triangle + 11] = vertex + 1;
            triangles[triangle + 12] = vertex + 2;
            triangles[triangle + 13] = next + 2;
            triangles[triangle + 14] = next;
            triangles[triangle + 15] = vertex + 2;
            triangles[triangle + 16] = next;
            triangles[triangle + 17] = vertex;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3 GetSiteMarkerPosition(Vector3 sitePosition)
    {
        float groundY = sitePosition.y;
        if (TryGetGroundY(sitePosition, sitePosition.y, out float sampledGroundY))
        {
            groundY = sampledGroundY;
        }

        return new Vector3(sitePosition.x, groundY + GroundClearance, sitePosition.z);
    }

    private static void ConformSiteSquareToGround(GameObject marker, int siteIndex,
        Vector3 sitePosition)
    {
        if (SiteSquaresConformed[siteIndex] && SiteSquarePositions[siteIndex] == sitePosition)
        {
            return;
        }

        SiteSquaresConformed[siteIndex] = true;
        SiteSquarePositions[siteIndex] = sitePosition;
        Transform? square = marker.transform.Find("BombSiteSquare");
        Mesh? mesh = square?.GetComponent<MeshFilter>()?.sharedMesh;
        if (square == null || mesh == null)
        {
            return;
        }

        Vector3[] vertices = mesh.vertices;
        for (int index = 0; index < vertices.Length; index++)
        {
            Vector3 vertex = vertices[index];
            Vector3 worldPosition = marker.transform.position
                + new Vector3(vertex.x, 0f, vertex.z);
            float groundY = sitePosition.y;
            if (TryGetGroundY(worldPosition, sitePosition.y, out float sampledGroundY))
            {
                groundY = sampledGroundY;
            }

            bool top = index % 4 >= 2;
            vertex.y = groundY + GroundClearance + (top ? SiteSquareHeight : 0f)
                - marker.transform.position.y;
            vertices[index] = vertex;
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private static bool TryGetGroundY(Vector3 position, float referenceY, out float groundY)
    {
        groundY = referenceY;
        Vector3 rayOrigin = new(position.x, referenceY + 4f, position.z);
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, 8f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float closestDifference = float.MaxValue;
        bool foundGround = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.GetComponentInParent<PlayerHealth>() != null)
            {
                continue;
            }

            float difference = Mathf.Abs(hit.point.y - referenceY);
            if (difference < closestDifference)
            {
                closestDifference = difference;
                groundY = hit.point.y;
                foundGround = true;
            }
        }

        return foundGround;
    }

    private static Mesh CreateSquareMesh()
    {
        Mesh mesh = new() { name = "SearchAndDestroySquareMesh" };
        Vector3[] vertices = new Vector3[16];
        int[] triangles = new int[24];
        float outer = SiteSquareSize * 0.5f;
        float inner = outer - SiteSquareThickness;
        Vector2[] outerCorners =
        {
            new(-outer, -outer), new(outer, -outer),
            new(outer, outer), new(-outer, outer)
        };
        Vector2[] innerCorners =
        {
            new(-inner, -inner), new(inner, -inner),
            new(inner, inner), new(-inner, inner)
        };

        for (int index = 0; index < 4; index++)
        {
            int vertex = index * 4;
            vertices[vertex] = new Vector3(outerCorners[index].x, 0f, outerCorners[index].y);
            vertices[vertex + 1] = new Vector3(innerCorners[index].x, 0f, innerCorners[index].y);
            vertices[vertex + 2] = new Vector3(outerCorners[index].x, SiteSquareHeight,
                outerCorners[index].y);
            vertices[vertex + 3] = new Vector3(innerCorners[index].x, SiteSquareHeight,
                innerCorners[index].y);

            int next = ((index + 1) % 4) * 4;
            int triangle = index * 6;
            triangles[triangle] = vertex + 2;
            triangles[triangle + 1] = next + 3;
            triangles[triangle + 2] = next + 2;
            triangles[triangle + 3] = vertex + 2;
            triangles[triangle + 4] = vertex + 3;
            triangles[triangle + 5] = next + 3;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void ConfigureRenderer(Renderer renderer, bool overlay)
    {
        Shader? shader = Shader.Find("Hidden/Internal-Colored")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            Material material = new(shader);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)(overlay ? CompareFunction.Always : CompareFunction.LessEqual));
            material.SetInt("_Cull", (int)CullMode.Off);
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = overlay ? (int)RenderQueue.Overlay : 3000;
            renderer.material = material;
        }

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static void SetActive(GameObject? target, bool active)
    {
        if (target != null && target)
        {
            target.SetActive(active);
        }
    }

    private static void Clear()
    {
        for (int index = 0; index < SiteMarkers.Length; index++)
        {
            if (SiteMarkers[index] != null && SiteMarkers[index])
            {
                Object.Destroy(SiteMarkers[index]);
                SiteMarkers[index] = null;
            }

            SiteSquaresConformed[index] = false;
        }

        if (_bomb != null && _bomb)
        {
            Object.Destroy(_bomb);
            _bomb = null;
        }
    }
}
