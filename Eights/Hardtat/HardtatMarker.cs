using UnityEngine;

namespace Eights;

internal static class HardtatMarker
{
    private const float ActiveAlpha = 0.8f;
    private static readonly Color NextMarkerColor = new(0.55f, 0.55f, 0.55f);
    private const float NextMarkerMinAlpha = 0.35f;
    private const float NextMarkerMaxAlpha = 0.75f;
    private const float OverheadMarkerSize = 0.525f;
    private static GameObject? _activeMarker;
    private static GameObject? _activeOverheadMarker;
    private static GameObject? _nextMarker;

    internal static void Update()
    {
        if (!GameModeManager.IsActive(GameMode.Hardtat)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || !HardtatState.TryGetCurrentObjective(out HardtatObjective current))
        {
            Clear();
            return;
        }

        Color activeColor = GetActiveColor();
        if (_activeMarker == null || !_activeMarker)
        {
            _activeMarker = CreateMarker("HardtatActiveMarker", activeColor);
        }
        PositionMarker(_activeMarker, current, activeColor);
        if (_activeOverheadMarker == null || !_activeOverheadMarker)
        {
            _activeOverheadMarker = CreateOverheadMarker(activeColor);
        }
        PositionOverheadMarker(_activeOverheadMarker, current, activeColor);

        if (HardtatState.IsWarningActive
            && HardtatState.TryGetNextObjective(out HardtatObjective next))
        {
            if (_nextMarker == null || !_nextMarker)
            {
                _nextMarker = FloatingObjectiveMarker.CreateOverheadDiamond(
                    "HardtatNextMarker",
                    new Color(NextMarkerColor.r, NextMarkerColor.g, NextMarkerColor.b,
                        NextMarkerMinAlpha));
            }

            float pulse = Mathf.Lerp(NextMarkerMinAlpha, NextMarkerMaxAlpha,
                (Mathf.Sin(Time.unscaledTime * 7f) + 1f) * 0.5f);
            PositionOverheadMarker(_nextMarker, next,
                new Color(NextMarkerColor.r, NextMarkerColor.g, NextMarkerColor.b, pulse));
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

    private static GameObject CreateMarker(string name, Color color)
    {
        return FloatingObjectiveMarker.CreateRing(name, color);
    }

    private static GameObject CreateOverheadMarker(Color color)
    {
        return FloatingObjectiveMarker.CreateOverheadDiamond("HardtatOverheadMarker", color);
    }

    private static void PositionOverheadMarker(GameObject marker,
        HardtatObjective objective, Color color)
    {
        FloatingObjectiveMarker.PositionOverhead(marker, objective.Position, color,
            OverheadMarkerSize);
    }

    private static void PositionMarker(GameObject marker, HardtatObjective objective,
        Color color)
    {
        FloatingObjectiveMarker.PositionRing(marker, objective.Position, objective.Radius,
            color);
    }

    private static Color GetActiveColor()
    {
        int controller = HardtatState.CurrentController;
        if (controller < 0)
        {
            return new Color(1f, 1f, 1f, ActiveAlpha);
        }

        return TeamColorPolicy.GetRelativeTeamColor(controller, ActiveAlpha);
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
    }
}