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
    private const float TargetAnnouncementVerticalOffset = 110f;
    private const float ScorePopupDuration = 1f;
    private const float ScorePopupVerticalOffset = -70f;
    private const int MaxDisplayedNameLength = 14;
    private static GameModeHud? _instance;
    private GameObject _panel = null!;
    private TextMeshProUGUI _announcement = null!;
    private TextMeshProUGUI _targetAnnouncement = null!;
    private TextMeshProUGUI _scorePopup = null!;
    private TextMeshProUGUI _scoreboard = null!;
    private float _nextRefreshTime;
    private float _announcementUntil;
    private float _targetAnnouncementUntil;
    private float _scorePopupUntil;
    private bool _hasLoggedVisibility;
    private bool _lastVisible;
    private string _lastVisibilityReason = string.Empty;

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

        GameObject targetAnnouncementObject = new("GameModeTargetAnnouncement");
        targetAnnouncementObject.transform.SetParent(transform, false);
        RectTransform targetAnnouncementRect = targetAnnouncementObject.AddComponent<RectTransform>();
        targetAnnouncementRect.anchorMin = new Vector2(0.5f, 0.5f);
        targetAnnouncementRect.anchorMax = new Vector2(0.5f, 0.5f);
        targetAnnouncementRect.pivot = new Vector2(0.5f, 0.5f);
        targetAnnouncementRect.sizeDelta = new Vector2(1200f, 160f);
        targetAnnouncementRect.anchoredPosition = new Vector2(0f, TargetAnnouncementVerticalOffset);
        _targetAnnouncement = targetAnnouncementObject.AddComponent<TextMeshProUGUI>();
        _targetAnnouncement.fontSize = 56f;
        _targetAnnouncement.fontStyle = FontStyles.Bold;
        _targetAnnouncement.richText = true;
        _targetAnnouncement.alignment = TextAlignmentOptions.Center;
        _targetAnnouncement.enableWordWrapping = true;
        _targetAnnouncement.outlineWidth = 0.2f;
        _targetAnnouncement.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        _targetAnnouncement.raycastTarget = false;
        targetAnnouncementObject.SetActive(false);

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

        if (_targetAnnouncement.gameObject.activeSelf
            && Time.unscaledTime >= _targetAnnouncementUntil)
        {
            _targetAnnouncement.gameObject.SetActive(false);
        }

        if (GameModeManager.IsMatchOver)
        {
            if (!_hasLoggedVisibility || _lastVisible || _lastVisibilityReason != "match-over")
            {
                _hasLoggedVisibility = true;
                _lastVisible = false;
                _lastVisibilityReason = "match-over";
                Plugin.Logger.LogInfo($"[GameModeHud] visible=false reason=match-over "
                    + $"mode={GameModeManager.ActiveMode} phase={GameModeManager.Phase} "
                    + $"round={GameModeManager.RoundId} players=0 scores={GunGameState.Progress.Count}");
            }

            _announcement.gameObject.SetActive(false);
            _targetAnnouncement.gameObject.SetActive(false);
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
        bool isCustomMode = GameModeManager.IsCustomMode;
        bool shouldHideCustomHud = GameModeManager.ShouldHideCustomHud;
        bool isMatchOver = GameModeManager.IsMatchOver;
        PauseManager? pauseManager = PauseManager.Instance;
        int connectedPlayerCount = 0;
        string visibilityReason;
        if (!isCustomMode)
        {
            visibilityReason = "custom-mode-off";
        }
        else if (shouldHideCustomHud)
        {
            visibilityReason = "mode-hides-hud";
        }
        else if (isMatchOver)
        {
            visibilityReason = "match-over";
        }
        else if (pauseManager == null)
        {
            visibilityReason = "pause-manager-missing";
        }
        else
        {
            connectedPlayerCount = PlayerLookup.GetConnectedPlayerIds().Count;
            bool activeRoundWithPlayers = GameModeManager.Phase == GameModePhase.ActiveRound
                && connectedPlayerCount > 0;
            if (pauseManager.inVictoryMenu)
            {
                visibilityReason = "victory-menu";
            }
            else if (pauseManager.inMainMenu && !activeRoundWithPlayers)
            {
                visibilityReason = "main-menu";
            }
            else
            {
                visibilityReason = connectedPlayerCount > 0 ? "visible" : "no-connected-players";
            }
        }

        bool visible = visibilityReason == "visible";
        if (!_hasLoggedVisibility || visible != _lastVisible || visibilityReason != _lastVisibilityReason)
        {
            _hasLoggedVisibility = true;
            _lastVisible = visible;
            _lastVisibilityReason = visibilityReason;
            Plugin.Logger.LogInfo($"[GameModeHud] visible={visible} reason={visibilityReason} "
                + $"mode={GameModeManager.ActiveMode} phase={GameModeManager.Phase} "
                + $"round={GameModeManager.RoundId} players={connectedPlayerCount} "
                + $"scores={GunGameState.Progress.Count}");
        }

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

        _instance._targetAnnouncement.text = text;
        _instance._targetAnnouncementUntil = Time.unscaledTime + Mathf.Max(0f, durationSeconds);
        _instance._targetAnnouncement.gameObject.SetActive(true);
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
        Dictionary<int, int> scores;
        bool crownFirst;
        string header;
        if (GameModeManager.IsActive(GameMode.FreeForAll))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + " - " + FFAState.KillsToWin;
            scores = FFAState.Kills;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.Juggernaut))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + " - " + JuggernautState.PointsToWin;
            scores = JuggernautState.Points;
            crownFirst = true;
        }
        else if (GameModeManager.IsActive(GameMode.SniperBattle))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + " - " + SniperBattleState.PointsToWin;
            scores = SniperBattleState.Points;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.KillTheRat))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + " - " + KillTheRatState.PointsToWin;
            scores = KillTheRatState.Points;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.HotPotato))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + " - " + HotPotatoState.KillsToWin;
            scores = HotPotatoState.Kills;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.Infidel))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + " - " + InfidelState.KillsToWin;
            scores = InfidelState.Scores;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.OneInTheChamber))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + " - "
                + OneInTheChamberState.PointsToWin + "  Round " + OneInTheChamberState.SubRoundId
                + "  " + OneInTheChamberState.AliveCount + " alive";
            scores = OneInTheChamberState.Scores;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.Default))
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + " - "
                + DefaultGameModeState.PointsToWin + "  Round " + DefaultGameModeState.SubRoundId
                + "  " + DefaultGameModeState.AliveCount + " alive";
            scores = DefaultGameModeState.Scores;
            crownFirst = false;
        }
        else
        {
            header = GameModeManager.GetModeLabelMarkup(GameModeManager.ActiveMode) + " - " + GunGameState.ScoreLimit;
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