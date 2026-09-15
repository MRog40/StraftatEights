using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyMarker
{
    private const float SiteRadius = 3f;
    private const float SiteOverheadMarkerSize = 0.8f;
    private const float BombMarkerHeight = 2.8f;
    private const float BombMarkerSize = 0.6f;
    private const float BombGroundMarkerWidth = 0.25f;
    private const float BombGroundMarkerThickness = 0.1f;
    private const float BombGroundMarkerHeight = 0.01f;
    private static readonly GameObject?[] SiteMarkers = new GameObject?[2];
    private static GameObject? _bomb;
    private static Mesh? _bombDiamondMesh;
    private static Mesh? _bombSquareMesh;
    private static AudioClip? _bombBeepClip;
    private static float _nextBombBeepTime;
    private static bool _bombWasPlanted;

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
            ConfigureRenderer(renderer, true);
            renderer.material.color = Color.black;
            AudioSource audio = _bomb.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;
            audio.volume = 0.65f;
        }

        bool carried = SearchAndDestroyState.BombStatus == SearchAndDestroyBombStatus.Carried;
        _bomb.GetComponent<MeshFilter>()!.sharedMesh = GetBombMesh(carried);
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
