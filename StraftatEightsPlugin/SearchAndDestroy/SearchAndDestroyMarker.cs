using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyMarker
{
    private const float SiteMarkerHeight = 3f;
    private const float SiteMarkerSize = 0.8f;
    private const float RingRadius = 1.05f;
    private const float RingThickness = 0.08f;
    private const float RingHeight = 0.05f;
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

        GameObject ring = new("OffenseMarker");
        ring.transform.SetParent(root.transform, false);
        ring.transform.localPosition = Vector3.up * 0.06f;
        MeshFilter ringFilter = ring.AddComponent<MeshFilter>();
        ringFilter.sharedMesh = CreateRingMesh();
        MeshRenderer ringRenderer = ring.AddComponent<MeshRenderer>();
        ConfigureRenderer(ringRenderer, true);
        ringRenderer.material.color = new Color32(220, 45, 45, 210);

        GameObject diamond = new("DefenseMarker");
        diamond.transform.SetParent(root.transform, false);
        diamond.transform.localPosition = Vector3.up * SiteMarkerHeight;
        diamond.transform.localScale = Vector3.one * SiteMarkerSize;
        MeshFilter diamondFilter = diamond.AddComponent<MeshFilter>();
        diamondFilter.sharedMesh = CreateDiamondMesh();
        MeshRenderer diamondRenderer = diamond.AddComponent<MeshRenderer>();
        ConfigureRenderer(diamondRenderer, true);
        diamondRenderer.material.color = new Color32(45, 110, 235, 235);

        GameObject labelObject = new("SiteLabel");
        labelObject.transform.SetParent(root.transform, false);
        labelObject.transform.localPosition = Vector3.up * (SiteMarkerHeight + 0.25f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = siteIndex == 0 ? "A" : "B";
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.characterSize = 0.45f;
        label.fontSize = 48;
        label.color = Color.white;
        label.fontStyle = FontStyle.Bold;
        MeshRenderer labelRenderer = label.GetComponent<MeshRenderer>();
        ConfigureRenderer(labelRenderer, true);
        labelRenderer.material.color = Color.white;

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

    private static Mesh CreateRingMesh()
    {
        Mesh mesh = new() { name = "SearchAndDestroyRingMesh" };
        Vector3[] vertices = new Vector3[RingSegments * 4];
        int[] triangles = new int[RingSegments * 24];
        float innerRadius = RingRadius - RingThickness;
        for (int index = 0; index < RingSegments; index++)
        {
            float angle = index * Mathf.PI * 2f / RingSegments;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            int vertex = index * 4;
            vertices[vertex] = new Vector3(x * RingRadius, 0f, z * RingRadius);
            vertices[vertex + 1] = new Vector3(x * innerRadius, 0f, z * innerRadius);
            vertices[vertex + 2] = new Vector3(x * RingRadius, RingHeight, z * RingRadius);
            vertices[vertex + 3] = new Vector3(x * innerRadius, RingHeight, z * innerRadius);

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
