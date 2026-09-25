using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Eights;

internal static class MapLocationDebug
{
    private const float SpawnLineHeight = 3f;
    private const float SpawnLineRadius = 0.06f;
    private const float HardpointLabelHeight = 4f;
    private const float HardpointLabelFontSize = 5f;
    private const float HardpointLabelWidth = 4f;
    private const float HardpointLabelHeightUnits = 2f;
    private const float BombSiteRadius = 3f;
    private const float BombSiteOverheadMarkerSize = 0.8f;
    private static readonly Color MarkerColor = new(1f, 1f, 1f, 0.9f);
    private static readonly Color TextColor = Color.white;
    private static readonly List<SpawnMarker> SpawnMarkers = new();
    private static readonly List<HardpointMarker> HardpointMarkers = new();
    private static readonly List<BombMarker> BombMarkers = new();
    private static bool _visible;
    private static string _mapName = string.Empty;
    private static bool _markersBuilt;

    private sealed class SpawnMarker
    {
        internal GameObject Line = null!;
        internal Vector3 Position;
    }

    private sealed class HardpointMarker
    {
        internal GameObject Ring = null!;
        internal TextMeshPro Label = null!;
        internal Vector3 Position;
        internal float Radius;
    }

    private sealed class BombMarker
    {
        internal GameObject Ring = null!;
        internal GameObject Overhead = null!;
        internal Vector3 Position;
    }

    internal static void Toggle()
    {
        _visible = !_visible;
        if (!_visible)
        {
            Clear();
        }
    }

    internal static void Update()
    {
        if (!_visible)
        {
            return;
        }

        string mapName = SceneManager.GetActiveScene().name;
        if (!_markersBuilt || !string.Equals(_mapName, mapName, System.StringComparison.Ordinal))
        {
            Clear();
            _mapName = mapName;
            _markersBuilt = true;
            if (MapDefinitions.TryGet(mapName, out MapDefinition definition))
            {
                Build(definition);
            }
        }

        PositionMarkers();
    }

    private static void Build(MapDefinition definition)
    {
        for (int index = 0; index < definition.SpawnPoints.Count; index++)
        {
            SpawnMarkers.Add(new SpawnMarker
            {
                Line = CreateVerticalLine($"MapDebugSpawn_{index}"),
                Position = definition.SpawnPoints[index]
            });
        }

        for (int index = 0; index < definition.HardpointObjectives.Count; index++)
        {
            HardpointObjective objective = definition.HardpointObjectives[index];
            HardpointMarkers.Add(new HardpointMarker
            {
                Ring = FloatingObjectiveMarker.CreateRing(
                    $"MapDebugHardpoint_{index}", MarkerColor),
                Label = CreateLabel((index + 1).ToString(CultureInfo.InvariantCulture),
                    $"MapDebugHardpointLabel_{index}"),
                Position = objective.Position,
                Radius = objective.Radius
            });
        }

        for (int index = 0; index < definition.SndObjectives.Count; index++)
        {
            BombMarkers.Add(new BombMarker
            {
                Ring = FloatingObjectiveMarker.CreateRing(
                    $"MapDebugBombSite_{index}", MarkerColor),
                Overhead = index == 0
                    ? FloatingObjectiveMarker.CreateOverheadChevron(
                        $"MapDebugBombSiteChevron_{index}", MarkerColor)
                    : FloatingObjectiveMarker.CreateOverheadDiamond(
                        $"MapDebugBombSiteDiamond_{index}", MarkerColor),
                Position = definition.SndObjectives[index]
            });
        }
    }

    private static void PositionMarkers()
    {
        foreach (SpawnMarker marker in SpawnMarkers)
        {
            PositionVerticalLine(marker.Line, marker.Position, SpawnLineHeight);
        }

        foreach (HardpointMarker marker in HardpointMarkers)
        {
            FloatingObjectiveMarker.PositionRing(marker.Ring, marker.Position, marker.Radius,
                MarkerColor);
            PositionLabel(marker.Label, marker.Position);
        }

        foreach (BombMarker marker in BombMarkers)
        {
            FloatingObjectiveMarker.PositionRing(marker.Ring, marker.Position, BombSiteRadius,
                MarkerColor);
            FloatingObjectiveMarker.PositionOverhead(marker.Overhead, marker.Position,
                MarkerColor, BombSiteOverheadMarkerSize);
        }
    }

    private static GameObject CreateVerticalLine(string name,
        float height = SpawnLineHeight, float radius = SpawnLineRadius)
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        line.name = name;
        Collider? collider = line.GetComponent<Collider>();
        if (collider != null)
        {
            Object.Destroy(collider);
        }

        line.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        Renderer renderer = line.GetComponent<Renderer>();
        ConfigureOverlayRenderer(renderer, MarkerColor);
        return line;
    }

    private static TextMeshPro CreateLabel(string value, string name)
    {
        GameObject labelObject = new(name);
        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        label.text = value;
        label.fontSize = HardpointLabelFontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = TextColor;
        label.fontStyle = FontStyles.Bold;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.rectTransform.sizeDelta = new Vector2(HardpointLabelWidth,
            HardpointLabelHeightUnits);
        return label;
    }

    private static void PositionVerticalLine(GameObject line, Vector3 position, float height)
    {
        line.SetActive(true);
        line.transform.SetPositionAndRotation(
            FloatingObjectiveMarker.CalculateMarkerPosition(position), Quaternion.identity);
        line.transform.localScale = new Vector3(line.transform.localScale.x,
            height * 0.5f, line.transform.localScale.z);
    }

    private static void PositionLabel(TextMeshPro label, Vector3 position)
    {
        label.gameObject.SetActive(true);
        label.transform.position = FloatingObjectiveMarker.CalculateMarkerPosition(position)
            + Vector3.up * HardpointLabelHeight;
        Camera? camera = Camera.main;
        if (camera != null)
        {
            label.transform.rotation = Quaternion.LookRotation(
                camera.transform.position - label.transform.position, Vector3.up);
        }
    }

    private static void ConfigureOverlayRenderer(Renderer renderer, Color color)
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
            material.SetInt("_ZTest", (int)CompareFunction.Always);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = (int)RenderQueue.Overlay;
            material.color = color;
            renderer.material = material;
        }

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static void Clear()
    {
        foreach (SpawnMarker marker in SpawnMarkers)
        {
            Destroy(marker.Line);
        }
        SpawnMarkers.Clear();

        foreach (HardpointMarker marker in HardpointMarkers)
        {
            FloatingObjectiveMarker.Release(marker.Ring);
            Destroy(marker.Ring);
            Destroy(marker.Label.gameObject);
        }
        HardpointMarkers.Clear();

        foreach (BombMarker marker in BombMarkers)
        {
            FloatingObjectiveMarker.Release(marker.Ring);
            Destroy(marker.Ring);
            Destroy(marker.Overhead);
        }
        BombMarkers.Clear();

        _markersBuilt = false;
    }

    private static void Destroy(GameObject marker)
    {
        if (marker != null && marker)
        {
            Object.Destroy(marker);
        }
    }
}
