using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyMarker
{
    private const float SiteRadius = 3f;
    private const float SiteOverheadMarkerSize = 0.8f;
    private const float BombMarkerHeight = 2.8f;
    private const float BombMarkerSize = 0.6f;
    private const float BombGroundMarkerWidth = 0.4f;
    private const float BombGroundMarkerThickness = 0.2f;
    private const float BombGroundMarkerHeight = 0.01f;
    private const int ElectricArcCount = 4;
    private const int ElectricArcPointCount = 7;
    private static readonly GameObject?[] SiteMarkers = new GameObject?[2];
    private static readonly List<LineRenderer> ElectricArcs = new();
    private static GameObject? _bomb;
    private static Mesh? _bombDiamondMesh;
    private static Mesh? _bombSquareMesh;
    private static AudioClip? _bombBeepClip;
    private static float _nextBombBeepTime;
    private static bool _bombWasPlanted;
    private static Material? _electricMaterial;

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
            ResetBombAudio();
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
            ConfigureRenderer(renderer,
                SearchAndDestroyState.BombStatus == SearchAndDestroyBombStatus.Carried);
            renderer.material.color = Color.black;
            AudioSource audio = _bomb.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;
            audio.volume = 0.975f;
            CreateElectricArcs(_bomb.transform);
        }

        bool carried = SearchAndDestroyState.BombStatus == SearchAndDestroyBombStatus.Carried;
        _bomb.GetComponent<MeshFilter>()!.sharedMesh = GetBombMesh(carried);
        _bomb.GetComponent<MeshRenderer>()!.material.SetInt("_ZTest",
            (int)(carried ? CompareFunction.Always : CompareFunction.LessEqual));
        if (carried)
        {
            _bomb.transform.SetPositionAndRotation(position + Vector3.up * BombMarkerHeight,
                Quaternion.identity);
            _bomb.transform.localScale = Vector3.one * BombMarkerSize;
        }
        else
        {
            float groundY = position.y;
            if (FloatingObjectiveMarker.TryGetGroundY(position, position.y, out float sampledGroundY))
            {
                groundY = sampledGroundY;
            }

            _bomb.transform.SetPositionAndRotation(
                new Vector3(position.x, groundY + BombGroundMarkerHeight, position.z),
                Quaternion.identity);
            _bomb.transform.localScale = Vector3.one;
        }
        _bomb.SetActive(true);
        UpdateElectricArcs(SearchAndDestroyState.BombStatus == SearchAndDestroyBombStatus.Planted);

        if (SearchAndDestroyState.BombStatus != SearchAndDestroyBombStatus.Planted)
        {
            ResetBombAudio();
            return;
        }

        if (!_bombWasPlanted)
        {
            _bombWasPlanted = true;
            _nextBombBeepTime = 0f;
        }

        if (Time.unscaledTime >= _nextBombBeepTime)
        {
            _bomb.GetComponent<AudioSource>()!.PlayOneShot(GetBombBeepClip());
            _nextBombBeepTime = Time.unscaledTime + 1f;
        }
    }

    private static AudioClip GetBombBeepClip()
    {
        if (_bombBeepClip != null)
        {
            return _bombBeepClip;
        }

        const int sampleRate = 44100;
        const float duration = 0.12f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        for (int index = 0; index < sampleCount; index++)
        {
            float progress = index / (float)sampleCount;
            float envelope = Mathf.Sin(progress * Mathf.PI);
            samples[index] = Mathf.Sin(2f * Mathf.PI * 880f * index / sampleRate)
                * envelope * 0.35f;
        }

        _bombBeepClip = AudioClip.Create("SearchAndDestroyBombBeep", sampleCount,
            1, sampleRate, false);
        _bombBeepClip.SetData(samples, 0);
        return _bombBeepClip;
    }

    private static void ResetBombAudio()
    {
        _bombWasPlanted = false;
        _nextBombBeepTime = 0f;
        if (_bomb != null && _bomb)
        {
            _bomb.GetComponent<AudioSource>()?.Stop();
        }
    }

    private static void CreateElectricArcs(Transform parent)
    {
        ElectricArcs.Clear();
        for (int arcIndex = 0; arcIndex < ElectricArcCount; arcIndex++)
        {
            GameObject arcObject = new($"BombElectricArc_{arcIndex}");
            arcObject.transform.SetParent(parent, false);
            LineRenderer arc = arcObject.AddComponent<LineRenderer>();
            arc.useWorldSpace = false;
            arc.positionCount = ElectricArcPointCount;
            arc.startWidth = 0.035f;
            arc.endWidth = 0.01f;
            arc.numCapVertices = 3;
            arc.shadowCastingMode = ShadowCastingMode.Off;
            arc.receiveShadows = false;
            if (GetElectricMaterial() != null)
            {
                arc.material = GetElectricMaterial();
            }

            arc.enabled = false;
            ElectricArcs.Add(arc);
        }
    }

    private static Material? GetElectricMaterial()
    {
        if (_electricMaterial != null)
        {
            return _electricMaterial;
        }

        Shader? shader = Shader.Find("Sprites/Default")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Standard");
        if (shader != null)
        {
            _electricMaterial = new Material(shader);
        }

        return _electricMaterial;
    }

    private static void UpdateElectricArcs(bool visible)
    {
        float time = Time.unscaledTime;
        for (int arcIndex = 0; arcIndex < ElectricArcs.Count; arcIndex++)
        {
            LineRenderer arc = ElectricArcs[arcIndex];
            if (arc == null || !arc)
            {
                continue;
            }

            arc.enabled = visible;
            if (!visible)
            {
                continue;
            }

            float phase = time * (2.5f + arcIndex * 0.35f) + arcIndex * 1.7f;
            for (int pointIndex = 0; pointIndex < ElectricArcPointCount; pointIndex++)
            {
                float progress = pointIndex / (float)(ElectricArcPointCount - 1);
                float angle = arcIndex * Mathf.PI * 0.5f + progress * 1.4f
                    + Mathf.Sin(phase + pointIndex * 2.1f) * 0.35f;
                float radius = 0.18f + progress * 0.28f;
                float jitter = Mathf.Sin(phase * 1.7f + pointIndex * 3.4f) * 0.08f;
                arc.SetPosition(pointIndex, new Vector3(
                    Mathf.Cos(angle) * (radius + jitter),
                    0.18f + progress * 0.9f,
                    Mathf.Sin(angle) * (radius + jitter)));
            }

            float alpha = 0.6f + Mathf.Sin(time * 8f + arcIndex) * 0.25f;
            Color color = new(0.25f, 0.85f, 1f, alpha);
            arc.startColor = color;
            arc.endColor = new Color(0.65f, 0.95f, 1f, alpha * 0.15f);
        }
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
        float halfWidth = BombGroundMarkerWidth * 0.5f;
        Mesh mesh = new()
        {
            name = "SearchAndDestroyBombSquareMesh",
            vertices = new[]
            {
                new Vector3(-halfWidth, 0f, -halfWidth),
                new Vector3(halfWidth, 0f, -halfWidth),
                new Vector3(halfWidth, 0f, halfWidth),
                new Vector3(-halfWidth, 0f, halfWidth),
                new Vector3(-halfWidth, BombGroundMarkerThickness, -halfWidth),
                new Vector3(halfWidth, BombGroundMarkerThickness, -halfWidth),
                new Vector3(halfWidth, BombGroundMarkerThickness, halfWidth),
                new Vector3(-halfWidth, BombGroundMarkerThickness, halfWidth)
            },
            triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7
            },
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
        UpdateElectricArcs(false);
        ElectricArcs.Clear();
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
            ResetBombAudio();
            Object.Destroy(_bomb);
            _bomb = null;
        }
    }
}
