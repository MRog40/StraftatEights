using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StraftatEightsPlugin;

internal sealed class GameModeHud : MonoBehaviour
{
    private const float RefreshInterval = 0.25f;
    private GameObject _panel = null!;
    private TextMeshProUGUI _text = null!;
    private float _nextRefreshTime;

    private void Awake()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        _panel = new GameObject("GameModePanel");
        _panel.transform.SetParent(transform, false);
        RectTransform panelRect = _panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-18f, -120f);
        _panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

        VerticalLayoutGroup layout = _panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.childAlignment = TextAnchor.UpperRight;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = _panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject textObject = new("GameModeText");
        textObject.transform.SetParent(_panel.transform, false);
        textObject.AddComponent<RectTransform>();
        _text = textObject.AddComponent<TextMeshProUGUI>();
        _text.fontSize = 22f;
        _text.richText = true;
        _text.enableWordWrapping = false;
        _text.alignment = TextAlignmentOptions.TopRight;
        _text.raycastTarget = false;
        _panel.SetActive(false);
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefreshTime)
        {
            return;
        }
        _nextRefreshTime = Time.unscaledTime + RefreshInterval;
        bool visible = GameModeManager.ActiveMode != GameMode.None
            && PauseManager.Instance != null && !PauseManager.Instance.inMainMenu
            && !PauseManager.Instance.inVictoryMenu && ClientInstance.playerInstances.Count > 0;
        _panel.SetActive(visible);
        if (visible)
        {
            _text.text = BuildText();
        }
    }

    private static string BuildText()
    {
        StringBuilder text = new();
        if (GameModeManager.IsActive(GameMode.FreeForAll))
        {
            text.Append("<b><color=#55CCFF>FFA</color></b>  ").Append(FFAState.KillsToWin).Append(" points to win");
            AppendRows(text, FFAState.Kills, false);
        }
        else if (GameModeManager.IsActive(GameMode.Juggernaut))
        {
            text.Append("<b><color=#FF6A00>JUGGERNAUT</color></b>");
            AppendRows(text, JuggernautState.Points, true);
        }
        return ClientInstance.ReplaceAllPlayerNameTags(text.ToString());
    }

    private static void AppendRows(StringBuilder text, Dictionary<int, int> scores, bool crownFirst)
    {
        List<KeyValuePair<int, int>> rows = ClientInstance.playerInstances.Keys
            .Select(id => new KeyValuePair<int, int>(id, scores.TryGetValue(id, out int score) ? score : 0))
            .OrderByDescending(row => crownFirst && row.Key == JuggernautState.CurrentJuggernautPlayerId)
            .ThenByDescending(row => row.Value)
            .ToList();
        foreach (KeyValuePair<int, int> row in rows)
        {
            text.Append('\n').Append("<color=#DDDDDD>").Append(PlayerLookup.GetPlayerNameTag(row.Key))
                .Append("  ").Append(row.Value);
            if (crownFirst && row.Key == JuggernautState.CurrentJuggernautPlayerId)
            {
                text.Append("  <b>JUG</b>");
            }
            text.Append("</color>");
        }
    }
}