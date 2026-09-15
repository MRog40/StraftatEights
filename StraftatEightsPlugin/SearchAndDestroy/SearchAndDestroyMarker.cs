using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyMarker
{
    private const float SiteRadius = 3f;
    private const float SiteOverheadMarkerSize = 0.8f;
    private const float BombMarkerHeight = 2.8f;
    private const float BombMarkerSize = 0.6f;
    private const float BombGroundMarkerSize = 0.25f;
    private const float BombGroundMarkerHeight = 0.01f;
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

        Color overheadColor = GetOverheadMarkerColor();
        for (int siteIndex = 0; siteIndex < 2; siteIndex++)
        {
            if (!SearchAndDestroyState.TryGetSitePosition(siteIndex, out Vector3 position))
            {
                SetActive(SiteMarkers[siteIndex], false);
                continue;
            }

            if (SiteMarkers[siteIndex] == null || !SiteMarkers[siteIndex]!)
            {
                SiteMarkers[siteIndex] = CreateSiteMarker(siteIndex, overheadColor);
            }

            Transform markerRoot = SiteMarkers[siteIndex]!.transform;
            GameObject ring = markerRoot.Find("BombSiteRing")!.gameObject;
            FloatingObjectiveMarker.PositionRing(ring, position, SiteRadius,
                new Color(1f, 1f, 1f, 0.8f));
            GameObject overheadMarker = markerRoot.Find("BombSiteOverhead")!.gameObject;
            FloatingObjectiveMarker.PositionOverhead(overheadMarker, position,
                overheadColor, SiteOverheadMarkerSize);
            SiteMarkers[siteIndex]!.SetActive(true);
        }

        UpdateBomb();
    }

    internal static void ResetState()
    {
        Clear();
    }

    private static GameObject CreateSiteMarker(int siteIndex, Color overheadColor)
    {
        GameObject root = new($"SearchAndDestroySite_{siteIndex}");
        GameObject ring = FloatingObjectiveMarker.CreateRing("BombSiteRing",
            new Color(1f, 1f, 1f, 0.8f));
        ring.transform.SetParent(root.transform, false);

        GameObject marker = siteIndex == 0
            ? FloatingObjectiveMarker.CreateOverheadCircle("CircleMarker", overheadColor)
            : FloatingObjectiveMarker.CreateOverheadDiamond("DiamondMarker", overheadColor);
        marker.name = "BombSiteOverhead";
        marker.transform.SetParent(root.transform, false);

        return root;
    }

    private static Color GetOverheadMarkerColor()
    {
        if (ClientInstance.Instance != null
            && TeamAssignment.TryGetTeamId(ClientInstance.Instance.PlayerId, out int teamId))
        {
            if (teamId == SearchAndDestroyState.OffensiveTeamId)
            {
                return new Color32(220, 45, 45, 235);
            }

            if (teamId == SearchAndDestroyState.DefensiveTeamId)
            {
                return new Color32(45, 110, 235, 235);
            }
        }

        return new Color32(220, 220, 220, 235);
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
