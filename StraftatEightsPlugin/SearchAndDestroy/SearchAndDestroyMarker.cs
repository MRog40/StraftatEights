using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyMarker
{
    private const float SiteMarkerHeight = 3f;
    private const float SiteMarkerSize = 0.8f;
    private const float SiteSquareSize = 2.4f;
    private const float SiteSquareThickness = 0.1f;
    private const float SiteSquareHeight = 0.05f;
    private const float CircleRadius = 0.55f;
    private const float CircleThickness = 0.08f;
    private const float CircleHeight = 0.05f;
    private const int RingSegments = 48;
    private static readonly GameObject?[] SiteMarkers = new GameObject?[2];
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

            SiteMarkers[siteIndex]!.transform.SetPositionAndRotation(position, Quaternion.identity);
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
        ConfigureRenderer(squareRenderer, true);
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
            _bomb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _bomb.name = "SearchAndDestroyBomb";
            _bomb.transform.localScale = Vector3.one;
            Renderer renderer = _bomb.GetComponent<Renderer>();
            ConfigureRenderer(renderer, false);
            renderer.material.color = new Color32(35, 35, 35, 255);
        }

        _bomb.transform.SetPositionAndRotation(position + Vector3.up * 0.5f, Quaternion.identity);
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
        int[] triangles = new int[RingSegments * 24];
        float innerRadius = CircleRadius - CircleThickness;
        for (int index = 0; index < RingSegments; index++)
        {
            float angle = index * Mathf.PI * 2f / RingSegments;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            int vertex = index * 4;
            vertices[vertex] = new Vector3(x * CircleRadius, 0f, z * CircleRadius);
            vertices[vertex + 1] = new Vector3(x * innerRadius, 0f, z * innerRadius);
            vertices[vertex + 2] = new Vector3(x * CircleRadius, CircleHeight, z * CircleRadius);
            vertices[vertex + 3] = new Vector3(x * innerRadius, CircleHeight, z * innerRadius);

            int next = ((index + 1) % RingSegments) * 4;
            int triangle = index * 24;
            triangles[triangle] = vertex + 2;
            triangles[triangle + 1] = next + 3;
            triangles[triangle + 2] = next + 2;
            triangles[triangle + 3] = vertex + 2;
            triangles[triangle + 4] = vertex + 3;
            triangles[triangle + 5] = next + 3;
            triangles[triangle + 6] = vertex;
            triangles[triangle + 7] = next;
            triangles[triangle + 8] = next + 1;
            triangles[triangle + 9] = vertex;
            triangles[triangle + 10] = next + 1;
            triangles[triangle + 11] = vertex + 1;
            triangles[triangle + 12] = vertex;
            triangles[triangle + 13] = next + 2;
            triangles[triangle + 14] = next;
            triangles[triangle + 15] = vertex;
            triangles[triangle + 16] = vertex + 2;
            triangles[triangle + 17] = next + 2;
            triangles[triangle + 18] = vertex + 1;
            triangles[triangle + 19] = next + 1;
            triangles[triangle + 20] = next + 3;
            triangles[triangle + 21] = vertex + 1;
            triangles[triangle + 22] = next + 3;
            triangles[triangle + 23] = vertex + 3;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
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
        }

        if (_bomb != null && _bomb)
        {
            Object.Destroy(_bomb);
            _bomb = null;
        }
    }
}
