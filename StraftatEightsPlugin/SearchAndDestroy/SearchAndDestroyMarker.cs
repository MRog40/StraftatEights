using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyMarker
{
    private const float SiteRadius = 2.5f;
    private const float SiteOverheadMarkerSize = 0.8f;
    private const float CircleRadius = 0.55f;
    private const float CircleHeight = 0.05f;
    private const float BombMarkerHeight = 2.8f;
    private const float BombMarkerSize = 0.6f;
    private const float BombGroundMarkerSize = 0.25f;
    private const float BombGroundMarkerHeight = 0.01f;
    private const int RingSegments = 48;
    private static readonly GameObject?[] SiteMarkers = new GameObject?[2];
    private static GameObject? _bomb;
    private static Mesh? _bombDiamondMesh;
    private static Mesh? _bombSquareMesh;

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

            Transform markerRoot = SiteMarkers[siteIndex]!.transform;
            GameObject ring = markerRoot.Find("BombSiteRing")!.gameObject;
            FloatingObjectiveMarker.PositionRing(ring, position, SiteRadius,
                new Color(1f, 1f, 1f, 0.8f));
            GameObject overheadMarker = markerRoot.Find("BombSiteOverhead")!.gameObject;
            Color overheadColor = siteIndex == 0
                ? new Color32(220, 45, 45, 235)
                : new Color32(45, 110, 235, 235);
            FloatingObjectiveMarker.PositionOverhead(overheadMarker, position,
                overheadColor, SiteOverheadMarkerSize);
            if (siteIndex == 0)
            {
                overheadMarker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
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
        GameObject ring = FloatingObjectiveMarker.CreateRing("BombSiteRing",
            new Color(1f, 1f, 1f, 0.8f));
        ring.transform.SetParent(root.transform, false);

        GameObject marker = new(siteIndex == 0 ? "CircleMarker" : "DiamondMarker");
        marker.name = "BombSiteOverhead";
        marker.transform.SetParent(root.transform, false);
        marker.transform.localRotation = siteIndex == 0
            ? Quaternion.Euler(90f, 0f, 0f)
            : Quaternion.identity;
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
            || (SearchAndDestroyState.BombStatus == SearchAndDestroyBombStatus.Carried
                && !CanSeeCarriedBombMarker())
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
            filter.sharedMesh = GetBombMesh(
                SearchAndDestroyState.BombStatus == SearchAndDestroyBombStatus.Carried);
            MeshRenderer renderer = _bomb.AddComponent<MeshRenderer>();
            ConfigureRenderer(renderer, true);
            renderer.material.color = new Color32(35, 35, 35, 255);
        }

        bool carried = SearchAndDestroyState.BombStatus == SearchAndDestroyBombStatus.Carried;
        _bomb.GetComponent<MeshFilter>()!.sharedMesh = GetBombMesh(carried);
        float markerHeight = carried ? BombMarkerHeight : BombGroundMarkerHeight;
        _bomb.transform.SetPositionAndRotation(position + Vector3.up * markerHeight,
            Quaternion.identity);
        _bomb.transform.localScale = Vector3.one
            * (carried ? BombMarkerSize : BombGroundMarkerSize);
        _bomb.SetActive(true);
    }

    private static bool CanSeeCarriedBombMarker()
    {
        if (ClientInstance.Instance == null
            || !TeamAssignment.TryGetTeamId(ClientInstance.Instance.PlayerId, out int localTeamId)
            || !TeamAssignment.TryGetTeamId(SearchAndDestroyState.BombCarrierPlayerId,
                out int carrierTeamId))
        {
            return false;
        }

        return localTeamId == carrierTeamId;
    }

    private static Mesh GetBombMesh(bool carried)
    {
        if (carried)
        {
            return _bombDiamondMesh ??= CreateDiamondMesh();
        }

        return _bombSquareMesh ??= CreateBombSquareMesh();
    }

    private static Mesh CreateBombSquareMesh()
    {
        Mesh mesh = new()
        {
            name = "SearchAndDestroyBombSquareMesh",
            vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f)
            },
            triangles = new[] { 0, 2, 1, 0, 3, 2 }
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
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
                Transform? ring = SiteMarkers[index]!.transform.Find("BombSiteRing");
                if (ring != null)
                {
                    FloatingObjectiveMarker.Release(ring.gameObject);
                }
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
