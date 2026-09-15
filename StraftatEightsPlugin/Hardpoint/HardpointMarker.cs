using System.Collections.Generic;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HardpointMarker
{
    private const float ActiveAlpha = 0.8f;
    private const float OverheadMarkerSize = 0.525f;
    private const int GeometryProbeSegments = 32;
    private const float GeometryProbeMargin = 0.02f;
    private static GameObject? _activeMarker;
    private static GameObject? _activeOverheadMarker;
    private static GameObject? _nextMarker;
    private static Renderer? _nextRenderer;

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
        PositionMarker(_activeMarker, current, activeColor);
        if (_activeOverheadMarker == null || !_activeOverheadMarker)
        {
            _activeOverheadMarker = CreateOverheadMarker(activeColor);
        }
        PositionOverheadMarker(_activeOverheadMarker, current, activeColor);

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
            PositionMarker(_nextMarker, next, new Color(1f, 0.75f, 0.1f, pulse));
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

        ProbeGeometry("active", current);
        if (HardpointState.IsWarningActive
            && HardpointState.TryGetNextObjective(out HardpointObjective next))
        {
            ProbeGeometry("next", next);
        }
    }

    private static GameObject CreateMarker(string name, Color color)
    {
        return FloatingObjectiveMarker.CreateRing(name, color);
    }

    private static GameObject CreateOverheadMarker(Color color)
    {
        return FloatingObjectiveMarker.CreateOverheadDiamond("HardpointOverheadMarker", color);
    }

    private static void PositionOverheadMarker(GameObject marker,
        HardpointObjective objective, Color color)
    {
        FloatingObjectiveMarker.PositionOverhead(marker, objective.Position, color,
            OverheadMarkerSize);
    }

    private static void ProbeGeometry(string label, HardpointObjective objective)
    {
        float outerRadius = objective.Radius;
        float ringWidth = outerRadius * (1f - 0.975f);
        float centerRadius = outerRadius - ringWidth * 0.5f;
        float ringHeight = ringWidth;
        Vector3 ringBase = FloatingObjectiveMarker.CalculateMarkerPosition(objective.Position)
            - Vector3.up * FloatingObjectiveMarker.RingGroundClearance;
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
            if (FloatingObjectiveMarker.TryGetGroundY(center, objective.Position.y,
                out float sampledGroundY))
            {
                center.y = sampledGroundY + FloatingObjectiveMarker.RingGroundClearance
                    + ringHeight * 0.5f;
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

    private static void PositionMarker(GameObject marker, HardpointObjective objective,
        Color color)
    {
        FloatingObjectiveMarker.PositionRing(marker, objective.Position, objective.Radius,
            color);
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
            FloatingObjectiveMarker.Release(_activeMarker);
            Object.Destroy(_activeMarker);
        }
        _activeMarker = null;

        if (_activeOverheadMarker != null && _activeOverheadMarker)
        {
            Object.Destroy(_activeOverheadMarker);
        }
        _activeOverheadMarker = null;

        if (_nextMarker != null && _nextMarker)
        {
            FloatingObjectiveMarker.Release(_nextMarker);
            Object.Destroy(_nextMarker);
        }
        _nextMarker = null;
        _nextRenderer = null;
    }
}