using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Eights;

internal sealed class PlayerRadar : MonoBehaviour
{
    private const float RadarSize = 360f;
    private const float RadarInset = 18f;
    private const float RadarRadius = 144f;
    private const float RadarEdgeRadius = RadarSize * 0.5f - 8f;
    private const float RadarRange = 25f;
    private const float EnemyFlashDuration = 1.5f;
    private const float RadarSweepInterval = 10f;
    private const float RadarSweepDuration = 1f;
    private const int RadarTextureSize = 256;
    private const int AntialiasSamplesPerAxis = 4;
    private const int HardpointCurrentObjectiveMarkerId = 1;
    private const int HardpointNextObjectiveMarkerId = 2;
    private const int CaptureTheFlagObjectiveMarkerBaseId = 100;
    private const int SearchAndDestroySiteMarkerBaseId = 200;
    private const int SearchAndDestroyBombMarkerId = 202;
    private const int HuntersObjectiveMarkerId = 300;
    private const float ObjectiveMarkerSize = 18f;
    private const int ArrowTextureSize = 128;
    private const int DotTextureSize = 64;
    private const int ObjectiveIconTextureSize = 128;
    private const float PlayerArrowScale = 0.5f;
    private const float PlayerArrowWidth = 34f * PlayerArrowScale;
    private const float PlayerArrowHeight = 42f * PlayerArrowScale;
    private const float OutOfRangeLineLength = 22f;
    private const float OutOfRangeLineThickness = 6f;
    private const int EdgeArcTextureWidth = 44;
    private const int EdgeArcTextureHeight = 12;
    private static readonly Color32 RadarBackgroundColor = new(32, 32, 32, 26);
    private static readonly Color32 RadarBorderColor = new(88, 88, 88, 110);
    private static readonly Color RadarBlueColor = new(0.25f, 0.72f, 1f, 1f);
    private static readonly Color RadarGreenColor = new(0.25f, 1f, 0.68f, 1f);
    private static readonly Color RadarLightGreenColor = new(0.58f, 1f, 0.82f, 1f);
    private static readonly Color RadarLocalColor = RadarBlueColor;
    private static readonly HashSet<GameMode> RadarSweepModes = new()
    {
        GameMode.Infected,
        GameMode.HotPotInfected,
        GameMode.OneInTheChamber
    };
    private static Sprite? _backgroundSprite;
    private static Sprite? _arrowSprite;
    private static Sprite? _dotSprite;
    private static Sprite? _circleSprite;
    private static Sprite? _squareSprite;
    private static Sprite? _diamondSprite;
    private static Sprite? _chevronSprite;
    private static Sprite? _edgeArcSprite;
    private static PlayerRadar? _instance;
    private readonly Dictionary<int, Image> _markers = new();
    private readonly Dictionary<int, Image> _objectiveMarkers = new();
    private readonly Dictionary<int, float> _enemyFlashUntil = new();
    private readonly HashSet<int> _visiblePlayerIds = new();
    private readonly HashSet<int> _visibleObjectiveIds = new();
    private readonly List<int> _expiredEnemyPlayerIds = new();
    private RectTransform _radarRect = null!;
    private Image _centerMarker = null!;

    private void Awake()
    {
        _instance = this;
        GameObject radarObject = new("PlayerRadar", typeof(RectTransform));
        radarObject.transform.SetParent(transform, false);
        _radarRect = radarObject.GetComponent<RectTransform>();
        _radarRect.anchorMin = new Vector2(1f, 0f);
        _radarRect.anchorMax = new Vector2(1f, 0f);
        _radarRect.pivot = new Vector2(1f, 0f);
        _radarRect.sizeDelta = new Vector2(RadarSize, RadarSize);
        _radarRect.anchoredPosition = new Vector2(-RadarInset, RadarInset);

        Image background = radarObject.AddComponent<Image>();
        background.sprite = GetBackgroundSprite();
        background.color = RadarBackgroundColor;
        background.raycastTarget = false;
        Outline outline = radarObject.AddComponent<Outline>();
        outline.effectColor = RadarBorderColor;
        outline.effectDistance = new Vector2(2f, 2f);
        outline.useGraphicAlpha = true;

        GameObject centerObject = new("PlayerRadarCenter", typeof(RectTransform));
        centerObject.transform.SetParent(_radarRect, false);
        _centerMarker = centerObject.AddComponent<Image>();
        _centerMarker.sprite = GetArrowSprite();
        _centerMarker.color = RadarLocalColor;
        _centerMarker.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        _centerMarker.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _centerMarker.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        _centerMarker.rectTransform.sizeDelta = new Vector2(PlayerArrowWidth, PlayerArrowHeight);
        _centerMarker.rectTransform.anchoredPosition = Vector2.zero;
        _centerMarker.raycastTarget = false;
        radarObject.SetActive(false);
    }

    internal static void NotifyEnemyShot(int playerId)
    {
        if (playerId >= 0 && _instance != null && _instance)
        {
            _instance._enemyFlashUntil[playerId] = Time.unscaledTime + EnemyFlashDuration;
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void Update()
    {
        UpdateRadar();
    }

    private void UpdateRadar()
    {
        if (!GlobalModifiersState.PlayerRadarEnabled)
        {
            _enemyFlashUntil.Clear();
            SetRadarVisible(false);
            return;
        }

        PauseManager? pauseManager = PauseManager.Instance;
        bool visible = GameModeManager.IsCustomMode
            && GameModeManager.Phase == GameModePhase.ActiveRound
            && !GameModeManager.ShouldHideCustomHud
            && !GameModeManager.IsMatchOver
            && pauseManager?.inMainMenu != true
            && pauseManager?.inVictoryMenu != true;
        if (!visible)
        {
            _enemyFlashUntil.Clear();
            SetRadarVisible(false);
            return;
        }

        int localPlayerId = ClientInstance.Instance?.PlayerId ?? -1;
        PlayerHealth? localHealth = PlayerLookup.FindActivePlayerHealthById(localPlayerId);
        Transform? localTransform = GetPlayerTransform(localHealth);
        if (localTransform == null || !localTransform)
        {
            _enemyFlashUntil.Clear();
            SetRadarVisible(false);
            return;
        }

        SetRadarVisible(true);
        float now = Time.unscaledTime;
        ExpireEnemyFlashes(now);
        bool hasLocalTeam = TryGetRadarTeamId(localPlayerId, out int localTeamId);
        bool sweepActive = IsSweepActive();
        FirstPersonController? controller = localHealth?.controller;
        Transform viewTransform = controller?.playerCamera != null
            ? controller.playerCamera.transform
            : localTransform;
        Vector3 forward = viewTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = localTransform.forward;
            forward.y = 0f;
        }
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        _visiblePlayerIds.Clear();

        foreach (int playerId in PlayerLookup.GetConnectedPlayerIdsReadOnly())
        {
            if (playerId == localPlayerId)
            {
                continue;
            }

            int playerTeamId = -1;
            bool isTeammate = hasLocalTeam
                && TryGetRadarTeamId(playerId, out playerTeamId)
                && playerTeamId == localTeamId;
            PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(playerId);
            if (health == null || !health || health.health <= 0f)
            {
                _enemyFlashUntil.Remove(playerId);
                continue;
            }

            bool enemyFlashing = !isTeammate
                && _enemyFlashUntil.TryGetValue(playerId, out float flashUntil)
                && now < flashUntil;
            bool enemyRevealed = enemyFlashing || (!isTeammate && sweepActive);
            if (!isTeammate && !enemyRevealed)
            {
                continue;
            }

            Transform? playerTransform = GetPlayerTransform(health);
            if (playerTransform == null || !playerTransform)
            {
                continue;
            }

            Image marker = GetMarker(playerId);
            Vector3 offset = playerTransform.position - localTransform.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            Vector2 radarDirection = distance > 0.001f
                ? new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, forward)).normalized
                : Vector2.up;
            bool outOfRange = distance > RadarRange;
            if (outOfRange)
            {
                marker.sprite = GetEdgeArcSprite();
                marker.color = isTeammate
                    ? GetTeammateColor(playerTeamId)
                    : GetEnemyFlashColor(now);
                marker.rectTransform.sizeDelta = new Vector2(OutOfRangeLineLength,
                    OutOfRangeLineThickness);
                marker.rectTransform.anchoredPosition = radarDirection * RadarEdgeRadius;
                marker.rectTransform.localRotation = GetTangentRotation(radarDirection);
            }
            else
            {
                marker.sprite = GetArrowSprite();
                marker.color = isTeammate
                    ? GetTeammateColor(playerTeamId)
                    : IsRelativeTeamColorMode()
                        ? TeamColorPolicy.GetRelativeTeamColor(playerTeamId)
                        : GetEnemyFlashColor(now);
                marker.rectTransform.sizeDelta = new Vector2(PlayerArrowWidth,
                    PlayerArrowHeight);
                float radius = RadarRadius * Mathf.Clamp01(distance / RadarRange);
                marker.rectTransform.anchoredPosition = radarDirection * radius;
                Vector3 playerForward = playerTransform.forward;
                playerForward.y = 0f;
                Vector2 facingDirection = playerForward.sqrMagnitude > 0.001f
                    ? new Vector2(Vector3.Dot(playerForward, right),
                        Vector3.Dot(playerForward, forward)).normalized
                    : radarDirection;
                marker.rectTransform.localRotation = Quaternion.FromToRotation(Vector3.up,
                    new Vector3(facingDirection.x, facingDirection.y, 0f));
            }
            marker.gameObject.SetActive(true);
            _visiblePlayerIds.Add(playerId);
        }

        foreach (KeyValuePair<int, Image> marker in _markers)
        {
            if (!_visiblePlayerIds.Contains(marker.Key))
            {
                marker.Value.gameObject.SetActive(false);
            }
        }

        UpdateObjectiveMarkers(localTransform, right, forward);
    }

    private void UpdateObjectiveMarkers(Transform localTransform, Vector3 right,
        Vector3 forward)
    {
        _visibleObjectiveIds.Clear();

        if (GameModeManager.IsActive(GameMode.Hardpoint)
            && HardpointState.TryGetCurrentObjective(out HardpointObjective currentObjective))
        {
            SetObjectiveMarker(HardpointCurrentObjectiveMarkerId, currentObjective.Position,
                localTransform, right, forward, _visibleObjectiveIds, currentObjective.Radius);
            if (HardpointState.IsWarningActive
                && HardpointState.TryGetNextObjective(out HardpointObjective nextObjective))
            {
                SetObjectiveMarker(HardpointNextObjectiveMarkerId, nextObjective.Position,
                    localTransform, right, forward, _visibleObjectiveIds, nextObjective.Radius);
            }
        }

        if (GameModeManager.IsActive(GameMode.CaptureTheFlag))
        {
            for (int flagIndex = 0; flagIndex < 2; flagIndex++)
            {
                if (CaptureTheFlagState.TryGetFlagPosition(flagIndex, out Vector3 flagPosition))
                {
                    SetObjectiveMarker(CaptureTheFlagObjectiveMarkerBaseId + flagIndex,
                        flagPosition, localTransform, right, forward, _visibleObjectiveIds);
                }
            }
        }

        if (GameModeManager.IsActive(GameMode.SearchAndDestroy))
        {
            for (int siteIndex = 0; siteIndex < 2; siteIndex++)
            {
                if (SearchAndDestroyState.TryGetSitePosition(siteIndex, out Vector3 sitePosition))
                {
                    SetObjectiveMarker(SearchAndDestroySiteMarkerBaseId + siteIndex,
                        sitePosition, localTransform, right, forward, _visibleObjectiveIds);
                }
            }

            if (SearchAndDestroyState.TryGetBombPosition(out Vector3 bombPosition))
            {
                SetObjectiveMarker(SearchAndDestroyBombMarkerId, bombPosition,
                    localTransform, right, forward, _visibleObjectiveIds);
            }
        }

        if (HuntersState.IsTieBreakActive
            && HuntersState.TryGetTieBreakObjective(out HardpointObjective huntersObjective))
        {
            SetObjectiveMarker(HuntersObjectiveMarkerId, huntersObjective.Position,
                localTransform, right, forward, _visibleObjectiveIds);
        }

        foreach (KeyValuePair<int, Image> marker in _objectiveMarkers)
        {
            if (!_visibleObjectiveIds.Contains(marker.Key))
            {
                marker.Value.gameObject.SetActive(false);
            }
        }
    }

    private void SetObjectiveMarker(int markerId, Vector3 objectivePosition,
        Transform localTransform, Vector3 right, Vector3 forward,
        HashSet<int> visibleObjectiveIds, float? worldRadius = null)
    {
        Vector3 offset = objectivePosition - localTransform.position;
        offset.y = 0f;
        float distance = offset.magnitude;
        Vector2 radarDirection = distance > 0.001f
            ? new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, forward)).normalized
            : Vector2.up;
        bool outOfRange = distance > RadarRange;
        float radius = outOfRange
            ? RadarEdgeRadius
            : RadarRadius * Mathf.Clamp01(distance / RadarRange);
        Image marker = GetObjectiveMarker(markerId);
        marker.color = GetObjectiveMarkerColor(markerId);
        if (outOfRange)
        {
            marker.sprite = GetEdgeArcSprite();
            marker.rectTransform.sizeDelta = new Vector2(OutOfRangeLineLength,
                OutOfRangeLineThickness);
            marker.rectTransform.localRotation = GetTangentRotation(radarDirection);
        }
        else
        {
            marker.sprite = GetObjectiveMarkerSprite(markerId);
            float markerSize = worldRadius.HasValue
                ? worldRadius.Value * 2f * RadarRadius / RadarRange
                : ObjectiveMarkerSize;
            marker.rectTransform.sizeDelta = new Vector2(markerSize, markerSize);
            marker.rectTransform.localRotation = Quaternion.identity;
        }
        marker.rectTransform.anchoredPosition = radarDirection * radius;
        marker.gameObject.SetActive(true);
        visibleObjectiveIds.Add(markerId);
    }

    private static bool IsSweepActive()
    {
        if (!RadarSweepModes.Contains(GameModeManager.ActiveMode)
            || ModeTimeoutState.IsSuddenDeath
            || ModeTimeoutState.TimeRemaining <= 0f)
        {
            return false;
        }

        float elapsed = ModeTimeoutState.RoundElapsedSeconds;
        if (elapsed < RadarSweepInterval)
        {
            return false;
        }

        return elapsed % RadarSweepInterval < RadarSweepDuration;
    }

    private static bool TryGetRadarTeamId(int playerId, out int teamId)
    {
        if (GameModeManager.IsActive(GameMode.Infected))
        {
            bool roleStateAvailable = InfectedState.InitialInfectedPlayerId >= 0
                || InfectedState.InfectedPlayers.Count > 0;
            if (!roleStateAvailable)
            {
                teamId = -1;
                return false;
            }

            bool isInfected = InfectedState.InitialInfectedPlayerId == playerId
                || InfectedState.InfectedPlayers.Contains(playerId);
            teamId = isInfected ? TeamRules.BlueTeamId : TeamRules.VermillionTeamId;
            return true;
        }

        if (GameModeManager.IsActive(GameMode.HotPotInfected))
        {
            bool roleStateAvailable = HotPotInfectedState.InitialInfectedPlayerId >= 0
                || HotPotInfectedState.InfectedPlayers.Count > 0;
            if (!roleStateAvailable)
            {
                teamId = -1;
                return false;
            }

            bool isInfected = HotPotInfectedState.InitialInfectedPlayerId == playerId
                || HotPotInfectedState.InfectedPlayers.Contains(playerId);
            teamId = isInfected ? TeamRules.BlueTeamId : TeamRules.VermillionTeamId;
            return true;
        }

        return TeamAssignment.TryGetTeamId(playerId, out teamId);
    }

    private void ExpireEnemyFlashes(float now)
    {
        _expiredEnemyPlayerIds.Clear();
        foreach (KeyValuePair<int, float> flash in _enemyFlashUntil)
        {
            if (now >= flash.Value)
            {
                _expiredEnemyPlayerIds.Add(flash.Key);
            }
        }

        foreach (int playerId in _expiredEnemyPlayerIds)
        {
            _enemyFlashUntil.Remove(playerId);
        }
    }

    private static Color GetTeammateColor(int teamId)
    {
        if (IsRelativeTeamColorMode())
        {
            return TeamColorPolicy.GetRelativeTeamColor(teamId);
        }

        return teamId switch
        {
            TeamRules.BlueTeamId => RadarBlueColor,
            TeamRules.GreenTeamId => RadarLightGreenColor,
            _ => RadarGreenColor
        };
    }

    private static bool IsRelativeTeamColorMode()
    {
        return GameModeManager.IsActive(GameMode.CaptureTheFlag)
            || GameModeManager.IsActive(GameMode.Hardpoint);
    }

    private static Color GetEnemyFlashColor(float now)
    {
        float pulse = Mathf.PingPong(now * 8f, 1f);
        return new Color(1f, 0.06f, 0.06f, Mathf.Lerp(0.45f, 1f, pulse));
    }

    private static Quaternion GetTangentRotation(Vector2 radarDirection)
    {
        Vector2 tangent = new(-radarDirection.y, radarDirection.x);
        return Quaternion.FromToRotation(Vector3.right,
            new Vector3(tangent.x, tangent.y, 0f));
    }

    private void SetRadarVisible(bool visible)
    {
        if (_radarRect.gameObject.activeSelf == visible)
        {
            return;
        }

        _radarRect.gameObject.SetActive(visible);
    }

    private Image GetMarker(int playerId)
    {
        if (_markers.TryGetValue(playerId, out Image? marker))
        {
            return marker;
        }

        GameObject markerObject = new("PlayerRadarArrow", typeof(RectTransform));
        markerObject.transform.SetParent(_radarRect, false);
        marker = markerObject.AddComponent<Image>();
        marker.sprite = GetArrowSprite();
        marker.color = Color.white;
        marker.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        marker.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        marker.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        marker.rectTransform.sizeDelta = new Vector2(PlayerArrowWidth, PlayerArrowHeight);
        marker.raycastTarget = false;
        _markers[playerId] = marker;
        return marker;
    }

    private static Sprite GetObjectiveMarkerSprite(int markerId)
    {
        if (markerId == HardpointCurrentObjectiveMarkerId
            || markerId == HardpointNextObjectiveMarkerId)
        {
            return GetCircleSprite();
        }

        if (markerId == SearchAndDestroyBombMarkerId)
        {
            return GetSquareSprite();
        }

        if (markerId == SearchAndDestroySiteMarkerBaseId)
        {
            return GetChevronSprite();
        }

        if (markerId == SearchAndDestroySiteMarkerBaseId + 1)
        {
            return GetDiamondSprite();
        }

        return GetDotSprite();
    }

    private static Color GetObjectiveMarkerColor(int markerId)
    {
        return markerId == SearchAndDestroyBombMarkerId
            ? new Color32(150, 150, 150, 255)
            : Color.white;
    }

    private Image GetObjectiveMarker(int markerId)
    {
        if (_objectiveMarkers.TryGetValue(markerId, out Image? marker))
        {
            return marker;
        }

        GameObject markerObject = new("PlayerRadarObjective", typeof(RectTransform));
        markerObject.transform.SetParent(_radarRect, false);
        marker = markerObject.AddComponent<Image>();
        marker.sprite = GetDotSprite();
        marker.color = Color.white;
        marker.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        marker.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        marker.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        marker.rectTransform.sizeDelta = new Vector2(ObjectiveMarkerSize, ObjectiveMarkerSize);
        marker.raycastTarget = false;
        _objectiveMarkers[markerId] = marker;
        return marker;
    }

    private static Transform? GetPlayerTransform(PlayerHealth? health)
    {
        if (health == null || !health)
        {
            return null;
        }

        FirstPersonController? controller = health.controller;
        return controller != null && controller ? controller.transform : health.transform;
    }

    private static Sprite GetBackgroundSprite()
    {
        if (_backgroundSprite != null)
        {
            return _backgroundSprite;
        }

        Texture2D texture = new(RadarTextureSize, RadarTextureSize, TextureFormat.RGBA32, false)
        {
            name = "Eights Radar Background",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        Color32[] pixels = new Color32[RadarTextureSize * RadarTextureSize];
        float center = (RadarTextureSize - 1) * 0.5f;
        float radius = center - 1f;
        for (int y = 0; y < RadarTextureSize; y++)
        {
            for (int x = 0; x < RadarTextureSize; x++)
            {
                float distance = Mathf.Sqrt(Mathf.Pow(x - center, 2f)
                    + Mathf.Pow(y - center, 2f));
                byte alpha = distance >= radius
                    ? (byte)Mathf.Clamp(Mathf.RoundToInt((radius + 1f - distance) * 255f), 0, 255)
                    : (byte)255;
                pixels[y * RadarTextureSize + x] = new Color32(255, 255, 255, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        _backgroundSprite = Sprite.Create(texture,
            new Rect(0f, 0f, RadarTextureSize, RadarTextureSize),
            new Vector2(0.5f, 0.5f), 100f);
        _backgroundSprite.name = "Eights Radar Background";
        _backgroundSprite.hideFlags = HideFlags.DontSave;
        return _backgroundSprite;
    }

    private static Sprite GetArrowSprite()
    {
        if (_arrowSprite != null)
        {
            return _arrowSprite;
        }

        Texture2D texture = new(ArrowTextureSize, ArrowTextureSize, TextureFormat.RGBA32, false)
        {
            name = "Eights Radar Arrow",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        Color32[] pixels = new Color32[ArrowTextureSize * ArrowTextureSize];
        for (int y = 0; y < ArrowTextureSize; y++)
        {
            for (int x = 0; x < ArrowTextureSize; x++)
            {
                pixels[y * ArrowTextureSize + x] = GetArrowPixelColor(x, y);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        _arrowSprite = Sprite.Create(texture,
            new Rect(0f, 0f, ArrowTextureSize, ArrowTextureSize),
            new Vector2(0.5f, 0.5f), ArrowTextureSize);
        _arrowSprite.name = "Eights Radar Arrow";
        _arrowSprite.hideFlags = HideFlags.DontSave;
        return _arrowSprite;
    }

    private static Sprite GetDotSprite()
    {
        if (_dotSprite != null)
        {
            return _dotSprite;
        }

        Texture2D texture = new(DotTextureSize, DotTextureSize, TextureFormat.RGBA32, false)
        {
            name = "Eights Radar Center",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        Color32[] pixels = new Color32[DotTextureSize * DotTextureSize];
        float center = (DotTextureSize - 1) * 0.5f;
        float radius = DotTextureSize * 0.42f;
        for (int y = 0; y < DotTextureSize; y++)
        {
            for (int x = 0; x < DotTextureSize; x++)
            {
                pixels[y * DotTextureSize + x] = GetDotPixelColor(x, y, center, radius);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        _dotSprite = Sprite.Create(texture, new Rect(0f, 0f, DotTextureSize, DotTextureSize),
            new Vector2(0.5f, 0.5f), DotTextureSize);
        _dotSprite.name = "Eights Radar Center";
        _dotSprite.hideFlags = HideFlags.DontSave;
        return _dotSprite;
    }

    private static Sprite GetCircleSprite()
    {
        if (_circleSprite != null)
        {
            return _circleSprite;
        }

        _circleSprite = CreateObjectiveShapeSprite("Eights Radar Circle",
            ObjectiveMarkerShape.Circle);
        return _circleSprite;
    }

    private static Sprite GetSquareSprite()
    {
        if (_squareSprite != null)
        {
            return _squareSprite;
        }

        _squareSprite = CreateObjectiveShapeSprite("Eights Radar Square",
            ObjectiveMarkerShape.Square);
        return _squareSprite;
    }

    private static Sprite GetDiamondSprite()
    {
        if (_diamondSprite != null)
        {
            return _diamondSprite;
        }

        _diamondSprite = CreateObjectiveShapeSprite("Eights Radar Diamond",
            ObjectiveMarkerShape.Diamond);
        return _diamondSprite;
    }

    private static Sprite GetChevronSprite()
    {
        if (_chevronSprite != null)
        {
            return _chevronSprite;
        }

        _chevronSprite = CreateObjectiveShapeSprite("Eights Radar Chevron",
            ObjectiveMarkerShape.Chevron);
        return _chevronSprite;
    }

    private static Sprite CreateObjectiveShapeSprite(string name,
        ObjectiveMarkerShape shape)
    {
        Texture2D texture = new(ObjectiveIconTextureSize, ObjectiveIconTextureSize,
            TextureFormat.RGBA32, false)
        {
            name = name + " Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        Color32[] pixels = new Color32[ObjectiveIconTextureSize * ObjectiveIconTextureSize];
        float center = (ObjectiveIconTextureSize - 1) * 0.5f;
        float radius = center - 2f;
        for (int y = 0; y < ObjectiveIconTextureSize; y++)
        {
            for (int x = 0; x < ObjectiveIconTextureSize; x++)
            {
                pixels[y * ObjectiveIconTextureSize + x] = GetObjectivePixelColor(
                    x, y, center, radius, shape);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture,
            new Rect(0f, 0f, ObjectiveIconTextureSize, ObjectiveIconTextureSize),
            new Vector2(0.5f, 0.5f), ObjectiveIconTextureSize);
        sprite.name = name;
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    private static Color32 GetArrowPixelColor(int x, int y)
    {
        int coveredSamples = 0;
        float center = (ArrowTextureSize - 1) * 0.5f;
        for (int sampleY = 0; sampleY < AntialiasSamplesPerAxis; sampleY++)
        {
            float pixelY = y + GetSubpixelOffset(sampleY);
            float width = (1f - pixelY / (ArrowTextureSize - 1f))
                * ArrowTextureSize * 0.42f;
            for (int sampleX = 0; sampleX < AntialiasSamplesPerAxis; sampleX++)
            {
                float pixelX = x + GetSubpixelOffset(sampleX);
                if (Mathf.Abs(pixelX - center) <= width)
                {
                    coveredSamples++;
                }
            }
        }

        return GetCoverageColor(coveredSamples);
    }

    private static Color32 GetDotPixelColor(int x, int y, float center, float radius)
    {
        int coveredSamples = 0;
        for (int sampleY = 0; sampleY < AntialiasSamplesPerAxis; sampleY++)
        {
            float offsetY = y + GetSubpixelOffset(sampleY) - center;
            for (int sampleX = 0; sampleX < AntialiasSamplesPerAxis; sampleX++)
            {
                float offsetX = x + GetSubpixelOffset(sampleX) - center;
                if (offsetX * offsetX + offsetY * offsetY <= radius * radius)
                {
                    coveredSamples++;
                }
            }
        }

        return GetCoverageColor(coveredSamples);
    }

    private static Color32 GetObjectivePixelColor(int x, int y, float center, float radius,
        ObjectiveMarkerShape shape)
    {
        int coveredSamples = 0;
        for (int sampleY = 0; sampleY < AntialiasSamplesPerAxis; sampleY++)
        {
            float normalizedY = (y + GetSubpixelOffset(sampleY) - center) / radius;
            for (int sampleX = 0; sampleX < AntialiasSamplesPerAxis; sampleX++)
            {
                float normalizedX = (x + GetSubpixelOffset(sampleX) - center) / radius;
                Vector2 point = new(normalizedX, normalizedY);
                bool visible = shape switch
                {
                    ObjectiveMarkerShape.Circle => IsCirclePixel(point),
                    ObjectiveMarkerShape.Square => Mathf.Abs(point.x) <= 0.72f
                        && Mathf.Abs(point.y) <= 0.72f,
                    ObjectiveMarkerShape.Diamond => Mathf.Abs(point.x)
                        + Mathf.Abs(point.y) <= 0.96f,
                    _ => IsChevronPixel(point)
                };
                if (visible)
                {
                    coveredSamples++;
                }
            }
        }

        return GetCoverageColor(coveredSamples);
    }

    private static Color32 GetCoverageColor(int coveredSamples)
    {
        int totalSamples = AntialiasSamplesPerAxis * AntialiasSamplesPerAxis;
        byte alpha = (byte)(coveredSamples * 255 / totalSamples);
        return new Color32(255, 255, 255, alpha);
    }

    private static float GetSubpixelOffset(int sampleIndex)
    {
        return (sampleIndex + 0.5f) / AntialiasSamplesPerAxis;
    }

    private static bool IsCirclePixel(Vector2 point)
    {
        float distance = point.magnitude;
        return distance <= 1f && distance >= 0.92f;
    }

    private static bool IsChevronPixel(Vector2 point)
    {
        return IsNearSegment(point, new Vector2(-0.82f, -0.48f),
                new Vector2(0f, 0.68f), 0.13f)
            || IsNearSegment(point, new Vector2(0f, 0.68f),
                new Vector2(0.82f, -0.48f), 0.13f);
    }

    private static bool IsNearSegment(Vector2 point, Vector2 start, Vector2 end,
        float thickness)
    {
        Vector2 segment = end - start;
        float position = Mathf.Clamp01(Vector2.Dot(point - start, segment)
            / segment.sqrMagnitude);
        return Vector2.Distance(point, start + segment * position) <= thickness;
    }

    private enum ObjectiveMarkerShape
    {
        Circle,
        Square,
        Diamond,
        Chevron
    }

    private static Sprite GetEdgeArcSprite()
    {
        if (_edgeArcSprite != null)
        {
            return _edgeArcSprite;
        }

        Texture2D texture = new(EdgeArcTextureWidth, EdgeArcTextureHeight,
            TextureFormat.RGBA32, false)
        {
            name = "Eights Radar Edge Arc",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        Color32[] pixels = new Color32[EdgeArcTextureWidth * EdgeArcTextureHeight];
        float centerY = (EdgeArcTextureHeight - 1) * 0.5f;
        float capRadius = EdgeArcTextureHeight * 0.5f;
        float leftCapX = capRadius;
        float rightCapX = EdgeArcTextureWidth - 1f - capRadius;
        for (int y = 0; y < EdgeArcTextureHeight; y++)
        {
            for (int x = 0; x < EdgeArcTextureWidth; x++)
            {
                pixels[y * EdgeArcTextureWidth + x] = GetEdgeArcPixelColor(x, y,
                    centerY, capRadius, leftCapX, rightCapX);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        _edgeArcSprite = Sprite.Create(texture,
            new Rect(0f, 0f, EdgeArcTextureWidth, EdgeArcTextureHeight),
            new Vector2(0.5f, 0.5f), EdgeArcTextureWidth);
        _edgeArcSprite.name = "Eights Radar Edge Arc";
        _edgeArcSprite.hideFlags = HideFlags.DontSave;
        return _edgeArcSprite;
    }

    private static Color32 GetEdgeArcPixelColor(int x, int y, float centerY,
        float capRadius, float leftCapX, float rightCapX)
    {
        int coveredSamples = 0;
        for (int sampleY = 0; sampleY < AntialiasSamplesPerAxis; sampleY++)
        {
            float pixelY = y + GetSubpixelOffset(sampleY);
            for (int sampleX = 0; sampleX < AntialiasSamplesPerAxis; sampleX++)
            {
                float pixelX = x + GetSubpixelOffset(sampleX);
                float leftX = pixelX - leftCapX;
                float vertical = pixelY - centerY;
                float rightX = pixelX - rightCapX;
                bool inBody = pixelX >= leftCapX && pixelX <= rightCapX
                    && Mathf.Abs(vertical) <= capRadius;
                bool inCaps = leftX * leftX + vertical * vertical <= capRadius * capRadius
                    || rightX * rightX + vertical * vertical <= capRadius * capRadius;
                if (inBody || inCaps)
                {
                    coveredSamples++;
                }
            }
        }

        return GetCoverageColor(coveredSamples);
    }
}