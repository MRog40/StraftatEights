using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StraftatEightsPlugin;

// On-screen scoreboard for the Juggernaut mode showing everyone's points (so it's not necessary to
// spam chat to keep players updated). Added once to the plugin's persistent GameObject in
// InitializeJuggernaut, so it works the same regardless of who is currently the Juggernaut.
internal class JuggernautHud : MonoBehaviour
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

        _panel = new GameObject("JuggernautPanel");
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

        GameObject textObj = new("JuggernautText");
        textObj.transform.SetParent(_panel.transform, false);
        textObj.AddComponent<RectTransform>();
        _text = textObj.AddComponent<TextMeshProUGUI>();
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

        bool visible = JuggernautState.Enabled && JuggernautState.ShowScoreboard
            && PauseManager.Instance != null && !PauseManager.Instance.inMainMenu && !PauseManager.Instance.inVictoryMenu
            && ClientInstance.playerInstances.Count > 0;

        if (_panel.activeSelf != visible)
        {
            _panel.SetActive(visible);
        }
        if (visible)
        {
            _text.text = BuildText();
        }
    }

    private static string BuildText()
    {
        StringBuilder sb = new();
        sb.Append("<b><color=#FF6A00>JUGGERNAUT</color></b>");

        int jugId = JuggernautState.CurrentJuggernautPlayerId;
        List<KeyValuePair<int, int>> rows = new();
        foreach (KeyValuePair<int, ClientInstance> player in ClientInstance.playerInstances)
        {
            JuggernautState.Points.TryGetValue(player.Key, out int points);
            rows.Add(new KeyValuePair<int, int>(player.Key, points));
        }

        foreach (KeyValuePair<int, int> row in rows.OrderByDescending(r => r.Key == jugId).ThenByDescending(r => r.Value))
        {
            sb.Append('\n');
            string name = PlayerLookup.GetPlayerNameTag(row.Key);
            if (row.Key == jugId)
            {
                sb.Append("<color=#FF6A00><b>").Append(name).Append("</b>  ").Append(row.Value).Append("  <b>JUG</b></color>");
            }
            else
            {
                sb.Append("<color=#DDDDDD>").Append(name).Append("  ").Append(row.Value).Append("</color>");
            }
        }

        if (jugId < 0)
        {
            sb.Append("\n<color=#AAAAAA>First blood claims the crown!</color>");
        }

        return ClientInstance.ReplaceAllPlayerNameTags(sb.ToString());
    }
}
