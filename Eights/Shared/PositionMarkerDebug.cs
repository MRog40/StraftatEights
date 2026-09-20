using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eights;

internal static class PositionMarkerDebug
{
    internal static void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F9) && !Input.GetKeyDown(KeyCode.F10))
        {
            return;
        }

        if (ClientInstance.Instance == null)
        {
            Plugin.Logger.LogWarning("[PositionMarker] Cannot mark position: local player is unavailable.");
            return;
        }

        PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(ClientInstance.Instance.PlayerId);
        if (health == null || !health)
        {
            Plugin.Logger.LogWarning("[PositionMarker] Cannot mark position: active player object is unavailable.");
            return;
        }

        Vector3 position = health.transform.position;
        string map = SceneManager.GetActiveScene().name;
        string marker = Input.GetKeyDown(KeyCode.F9) ? "spawn" : "obj";
        Plugin.Logger.LogInfo($"[PositionMarker] Mark {marker} {map} ({Format(position.x)}, {Format(position.y)}, {Format(position.z)})");
    }

    private static string Format(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}