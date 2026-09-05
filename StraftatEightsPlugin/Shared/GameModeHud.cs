using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StraftatEightsPlugin;

internal sealed class GameModeHud : MonoBehaviour
{
    private const float RefreshInterval = 0.25f;
    private const float AnnouncementDuration = 3f;
    private const int MaxDisplayedNameLength = 14;
    private static GameModeHud? _instance;
    private GameObject _panel = null!;
    private TextMeshProUGUI _announcement = null!;
    private TextMeshProUGUI _header = null!;
    private Transform _rows = null!;
    private readonly List<GameObject> _rowObjects = new();
    private float _nextRefreshTime;
    private float _announcementUntil;

    private void Awake()
    {
        _instance = this;
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject announcementObject = new("GameModeAnnouncement");
        announcementObject.transform.SetParent(transform, false);
        RectTransform announcementRect = announcementObject.AddComponent<RectTransform>();
        announcementRect.anchorMin = new Vector2(0.5f, 0.5f);
        announcementRect.anchorMax = new Vector2(0.5f, 0.5f);
        announcementRect.pivot = new Vector2(0.5f, 0.5f);
        announcementRect.sizeDelta = new Vector2(1000f, 140f);
        announcementRect.anchoredPosition = new Vector2(0f, 120f);
        _announcement = announcementObject.AddComponent<TextMeshProUGUI>();
        _announcement.fontSize = 64f;
        _announcement.fontStyle = FontStyles.Bold;
        _announcement.richText = true;
        _announcement.alignment = TextAlignmentOptions.Center;
        _announcement.enableWordWrapping = false;
        _announcement.outlineWidth = 0.2f;
        _announcement.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        _announcement.raycastTarget = false;
        announcementObject.SetActive(false);

        _panel = new GameObject("GameModePanel");
        _panel.transform.SetParent(transform, false);
        RectTransform panelRect = _panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.sizeDelta = new Vector2(340f, 0f);
        panelRect.anchoredPosition = new Vector2(-18f, -120f);
        _panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

        VerticalLayoutGroup layout = _panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = _panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject headerObject = new("GameModeHeader");
        headerObject.transform.SetParent(_panel.transform, false);
        _header = headerObject.AddComponent<TextMeshProUGUI>();
        _header.fontSize = 22f;
        _header.richText = true;
        _header.enableWordWrapping = false;
        _header.alignment = TextAlignmentOptions.TopLeft;
        _header.raycastTarget = false;

        GameObject rowsObject = new("GameModeRows");
        rowsObject.transform.SetParent(_panel.transform, false);
        _rows = rowsObject.transform;
        VerticalLayoutGroup rowsLayout = rowsObject.AddComponent<VerticalLayoutGroup>();
        rowsLayout.childControlWidth = true;
        rowsLayout.childControlHeight = true;
        rowsLayout.childForceExpandWidth = true;
        rowsLayout.childForceExpandHeight = false;
        _panel.SetActive(false);
    }

    private void Update()
    {
        if (_announcement.gameObject.activeSelf && Time.unscaledTime >= _announcementUntil)
        {
            _announcement.gameObject.SetActive(false);
        }

        if (Time.unscaledTime < _nextRefreshTime)
        {
            return;
        }
        _nextRefreshTime = Time.unscaledTime + RefreshInterval;
        bool visible = GameModeManager.IsCustomMode && !GameModeManager.IsActive(GameMode.MichaelMeyers)
            && PauseManager.Instance != null && !PauseManager.Instance.inMainMenu
            && !PauseManager.Instance.inVictoryMenu && ClientInstance.playerInstances.Count > 0;
        _panel.SetActive(visible);
        if (visible)
        {
            RefreshScoreboard();
        }
    }

    internal static void AnnounceActiveMode()
    {
        if (_instance == null || !GameModeManager.IsCustomMode)
        {
            return;
        }

        _instance._announcement.text = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode);
        _instance._announcementUntil = Time.unscaledTime + AnnouncementDuration;
        _instance._announcement.gameObject.SetActive(true);
    }

    internal static void AnnounceTarget(string text)
    {
        if (_instance == null)
        {
            return;
        }

        _instance._announcement.text = text;
        _instance._announcementUntil = Time.unscaledTime + AnnouncementDuration;
        _instance._announcement.gameObject.SetActive(true);
    }

    private void RefreshScoreboard()
    {
        Dictionary<int, int> scores;
        bool crownFirst;
        if (GameModeManager.IsActive(GameMode.FreeForAll))
        {
            _header.text = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + "  " + FFAState.KillsToWin + " points to win";
            scores = FFAState.Kills;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.Juggernaut))
        {
            _header.text = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + "  " + JuggernautState.PointsToWin + " points to win";
            scores = JuggernautState.Points;
            crownFirst = true;
        }
        else if (GameModeManager.IsActive(GameMode.SniperBattle))
        {
            _header.text = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + "  " + SniperBattleState.PointsToWin + " points to win";
            scores = SniperBattleState.Points;
            crownFirst = false;
        }
        else
        {
            _header.text = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + "  " + GunGameState.ScoreLimit + " points to win";
            scores = GunGameState.Progress;
            crownFirst = false;
        }

        foreach (GameObject rowObject in _rowObjects)
        {
            Destroy(rowObject);
        }
        _rowObjects.Clear();

        List<KeyValuePair<int, int>> rows = ClientInstance.playerInstances.Keys
            .Select(id => new KeyValuePair<int, int>(id, scores.TryGetValue(id, out int score) ? score : 0))
            .OrderByDescending(row => crownFirst && row.Key == JuggernautState.CurrentJuggernautPlayerId)
            .ThenByDescending(row => row.Value)
            .ToList();
        foreach (KeyValuePair<int, int> row in rows)
        {
            AddScoreRow(row.Key, row.Value, crownFirst && row.Key == JuggernautState.CurrentJuggernautPlayerId);
        }
    }

    private void AddScoreRow(int playerId, int score, bool isJuggernaut)
    {
        GameObject rowObject = new("GameModeScoreRow");
        rowObject.transform.SetParent(_rows, false);
        HorizontalLayoutGroup rowLayout = rowObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.spacing = 8f;

        TextMeshProUGUI nameText = CreateRowText(rowObject.transform, TextAlignmentOptions.TopLeft);
        LayoutElement nameLayout = nameText.gameObject.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 1f;
        string playerName = ClientInstance.ReplaceAllPlayerNameTags(PlayerLookup.GetPlayerNameTag(playerId));
        if (playerName.Length > MaxDisplayedNameLength)
        {
            playerName = playerName.Substring(0, MaxDisplayedNameLength);
        }
        nameText.text = "<color=#DDDDDD>" + playerName + "</color>";

        TextMeshProUGUI scoreText = CreateRowText(rowObject.transform, TextAlignmentOptions.TopRight);
        scoreText.text = "<color=#DDDDDD>" + score + (isJuggernaut ? "  <b>JUG</b>" : string.Empty) + "</color>";
        _rowObjects.Add(rowObject);
    }

    private static TextMeshProUGUI CreateRowText(Transform parent, TextAlignmentOptions alignment)
    {
        GameObject textObject = new("GameModeRowText");
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = 22f;
        text.richText = true;
        text.enableWordWrapping = false;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }
}

[HarmonyLib.HarmonyPatch(typeof(PauseManager), "InvokeRoundStarted")]
internal static class PauseManager_GameModeAnnouncement_Patch
{
    private static void Postfix()
    {
        GameModeHud.AnnounceActiveMode();
    }
}