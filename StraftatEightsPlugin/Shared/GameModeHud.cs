using System.Collections.Generic;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StraftatEightsPlugin;

internal sealed class GameModeHud : MonoBehaviour
{
    private const float RefreshInterval = 0.25f;
    private const float AnnouncementDuration = 2f;
    private const float AnnouncementVerticalOffset = 260f;
    private const float ScorePopupDuration = 1f;
    private const float ScorePopupVerticalOffset = -70f;
    private const int MaxDisplayedNameLength = 14;
    private static GameModeHud? _instance;
    private GameObject _panel = null!;
    private TextMeshProUGUI _announcement = null!;
    private TextMeshProUGUI _scorePopup = null!;
    private TextMeshProUGUI _scoreboard = null!;
    private float _nextRefreshTime;
    private float _announcementUntil;
    private float _scorePopupUntil;

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
        announcementRect.anchoredPosition = new Vector2(0f, AnnouncementVerticalOffset);
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

        GameObject scorePopupObject = new("GameModeScorePopup");
        scorePopupObject.transform.SetParent(transform, false);
        RectTransform scorePopupRect = scorePopupObject.AddComponent<RectTransform>();
        scorePopupRect.anchorMin = new Vector2(0.5f, 0.5f);
        scorePopupRect.anchorMax = new Vector2(0.5f, 0.5f);
        scorePopupRect.pivot = new Vector2(0.5f, 0.5f);
        scorePopupRect.sizeDelta = new Vector2(400f, 80f);
        scorePopupRect.anchoredPosition = new Vector2(0f, ScorePopupVerticalOffset);
        _scorePopup = scorePopupObject.AddComponent<TextMeshProUGUI>();
        _scorePopup.fontSize = 44f;
        _scorePopup.fontStyle = FontStyles.Bold;
        _scorePopup.color = new Color32(130, 255, 150, 255);
        _scorePopup.richText = true;
        _scorePopup.alignment = TextAlignmentOptions.Center;
        _scorePopup.enableWordWrapping = false;
        _scorePopup.outlineWidth = 0.2f;
        _scorePopup.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        _scorePopup.raycastTarget = false;
        scorePopupObject.SetActive(false);

        _panel = new GameObject("GameModePanel");
        _panel.transform.SetParent(transform, false);
        RectTransform panelRect = _panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.sizeDelta = new Vector2(420f, 0f);
        panelRect.anchoredPosition = new Vector2(18f, -120f);
        _panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

        VerticalLayoutGroup layout = _panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = _panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject scoreboardObject = new("GameModeScoreboard", typeof(RectTransform));
        scoreboardObject.transform.SetParent(_panel.transform, false);
        _scoreboard = scoreboardObject.AddComponent<TextMeshProUGUI>();
        _scoreboard.fontSize = 22f;
        _scoreboard.richText = true;
        _scoreboard.enableWordWrapping = false;
        _scoreboard.alignment = TextAlignmentOptions.TopLeft;
        _scoreboard.raycastTarget = false;
        _panel.SetActive(false);
    }

    private void Update()
    {
        if (_scorePopup.gameObject.activeSelf && Time.unscaledTime >= _scorePopupUntil)
        {
            _scorePopup.gameObject.SetActive(false);
        }

        if (GameModeManager.IsMatchOver)
        {
            _announcement.gameObject.SetActive(false);
            _panel.SetActive(false);
            return;
        }

        if (_announcement.gameObject.activeSelf && Time.unscaledTime >= _announcementUntil)
        {
            _announcement.gameObject.SetActive(false);
        }

        if (Time.unscaledTime < _nextRefreshTime)
        {
            return;
        }
        _nextRefreshTime = Time.unscaledTime + RefreshInterval;
        bool visible = GameModeManager.IsCustomMode && !GameModeManager.ShouldHideCustomHud
            && !GameModeManager.IsMatchOver
            && PauseManager.Instance != null && !PauseManager.Instance.inMainMenu
            && !PauseManager.Instance.inVictoryMenu && PlayerLookup.GetConnectedPlayerIds().Count > 0;
        _panel.SetActive(visible);
        if (visible)
        {
            RefreshScoreboard();
        }
    }

    internal static void AnnounceActiveMode()
    {
        if (_instance == null || !GameModeManager.IsCustomMode || GameModeManager.IsMatchOver)
        {
            return;
        }

        _instance._announcement.text = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode);
        _instance._announcementUntil = Time.unscaledTime + AnnouncementDuration;
        _instance._announcement.gameObject.SetActive(true);
    }

    internal static void AnnounceTarget(string text)
    {
        AnnounceTarget(text, AnnouncementDuration);
    }

    internal static void AnnounceTarget(string text, float durationSeconds)
    {
        if (_instance == null || GameModeManager.IsMatchOver)
        {
            return;
        }

        _instance._announcement.text = text;
        _instance._announcementUntil = Time.unscaledTime + Mathf.Max(0f, durationSeconds);
        _instance._announcement.gameObject.SetActive(true);
    }

    internal static void ShowScorePopupForPlayer(int playerId, int amount)
    {
        if (playerId < 0 || amount <= 0 || !MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        if (ClientInstance.Instance != null && ClientInstance.Instance.PlayerId == playerId)
        {
            ShowScorePopup(amount);
            return;
        }

        if (ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client)
            && client != null && client && client.PlayerSteamID != 0)
        {
            MyceliumNetwork.RPCTarget(GameModeManager.ModId, nameof(Plugin.SyncScorePopup),
                new CSteamID(client.PlayerSteamID), ReliableType.Reliable, amount);
        }
    }

    internal static void ShowScorePopup(int amount)
    {
        if (_instance == null || amount <= 0)
        {
            return;
        }

        _instance._scorePopup.text = "+" + amount;
        _instance._scorePopupUntil = Time.unscaledTime + ScorePopupDuration;
        _instance._scorePopup.gameObject.SetActive(true);
    }

    private void RefreshScoreboard()
    {
        if (GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            _scoreboard.text = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode)
                + "  Survivors: " + MichaelMeyersState.SurvivorCount;
            return;
        }
        if (GameModeManager.IsActive(GameMode.OneInTheChamber))
        {
            _scoreboard.text = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode)
                + "  " + OneInTheChamberState.AliveCount + " players alive";
            return;
        }

        Dictionary<int, int> scores;
        bool crownFirst;
        string header;
        if (GameModeManager.IsActive(GameMode.FreeForAll))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + "  " + FFAState.KillsToWin + " points to win";
            scores = FFAState.Kills;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.Juggernaut))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + "  " + JuggernautState.PointsToWin + " points to win";
            scores = JuggernautState.Points;
            crownFirst = true;
        }
        else if (GameModeManager.IsActive(GameMode.SniperBattle))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + "  " + SniperBattleState.PointsToWin + " points to win";
            scores = SniperBattleState.Points;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.KillTheRat))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + "  " + KillTheRatState.PointsToWin + " points to win";
            scores = KillTheRatState.Points;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.HotPotato))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + "  " + HotPotatoState.KillsToWin + " points to win";
            scores = HotPotatoState.Kills;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.Infidel))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + "  " + InfidelState.KillsToWin + " points to win";
            scores = InfidelState.Scores;
            crownFirst = false;
        }
        else
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + "  " + GunGameState.ScoreLimit + " points to win";
            scores = GunGameState.Progress;
            crownFirst = false;
        }

        List<int> playerIds = PlayerLookup.GetConnectedPlayerIds();
        playerIds.Sort((left, right) =>
        {
            bool leftIsCrown = crownFirst && left == JuggernautState.CurrentJuggernautPlayerId;
            bool rightIsCrown = crownFirst && right == JuggernautState.CurrentJuggernautPlayerId;
            if (leftIsCrown != rightIsCrown)
            {
                return leftIsCrown ? -1 : 1;
            }

            scores.TryGetValue(left, out int leftScore);
            scores.TryGetValue(right, out int rightScore);
            return rightScore.CompareTo(leftScore);
        });

        StringBuilder text = new(header);
        foreach (int playerId in playerIds)
        {
            scores.TryGetValue(playerId, out int score);
            string playerName = ClientInstance.ReplaceAllPlayerNameTags(PlayerLookup.GetPlayerNameTag(playerId));
            if (playerName.Length > MaxDisplayedNameLength)
            {
                playerName = playerName.Substring(0, MaxDisplayedNameLength);
            }

            bool isJuggernaut = crownFirst && playerId == JuggernautState.CurrentJuggernautPlayerId;
            text.Append('\n').Append(isJuggernaut ? "<color=#FF6A00><b>" : "<color=#DDDDDD>")
                .Append(playerName).Append("  ").Append(score);
            if (isJuggernaut)
            {
                text.Append("  JUG</b>");
            }
            text.Append("</color>");
        }

        _scoreboard.text = text.ToString();
    }
}