using UnityEngine;

namespace Eights;

internal static class HuntModesMarker
{
    private const float MarkerAlpha = 0.8f;
    private const float OverheadMarkerSize = 0.525f;
    private static GameObject? _marker;
    private static GameObject? _overheadMarker;

    internal static void Update()
    {
        if (!HuntModesState.IsActive
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || !HuntModesState.IsTieBreakActive
            || !HuntModesState.TryGetTieBreakObjective(out HardtatObjective objective))
        {
            Clear();
            return;
        }

        Color color = GetColor();
        if (_marker == null || !_marker)
        {
            _marker = FloatingObjectiveMarker.CreateRing("HuntersTieBreakMarker", color);
        }
        FloatingObjectiveMarker.PositionRing(_marker, objective.Position, objective.Radius,
            color);

        if (_overheadMarker == null || !_overheadMarker)
        {
            _overheadMarker = FloatingObjectiveMarker.CreateOverheadDiamond(
                "HuntersTieBreakOverheadMarker", color);
        }
        FloatingObjectiveMarker.PositionOverhead(_overheadMarker, objective.Position,
            color, OverheadMarkerSize);
    }

    internal static void ResetState()
    {
        Clear();
    }

    private static Color GetColor()
    {
        int controller = HuntModesState.TieBreakController;
        if (controller < 0)
        {
            return new Color(1f, 1f, 1f, MarkerAlpha);
        }

        return TeamColorPolicy.GetRelativeTeamColor(controller, MarkerAlpha);
    }

    private static void Clear()
    {
        if (_marker != null && _marker)
        {
            FloatingObjectiveMarker.Release(_marker);
            Object.Destroy(_marker);
        }
        _marker = null;

        if (_overheadMarker != null && _overheadMarker)
        {
            Object.Destroy(_overheadMarker);
        }
        _overheadMarker = null;
    }
}
