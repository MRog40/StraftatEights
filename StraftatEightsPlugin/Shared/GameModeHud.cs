using System;
using System.Collections.Generic;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace StraftatEightsPlugin;

internal sealed class GameModeHud : MonoBehaviour
{
    private const float RefreshInterval = 0.1f;
    private const float AnnouncementDuration = 2f;
    private const float AnnouncementHorizontalMargin = 18f;
    private const float AnnouncementWidth = 600f;
    private const float AnnouncementGap = 16f;
    private const float ScorePopupDuration = 0.65f;
    private const float ScorePopupImpactDuration = 0.12f;
    private const float ScorePopupStartScale = 1.18f;
    private const float ScorePopupEndScale = 0.72f;
    private const float ScorePopupRise = 18f;
    private const float ScorePopupVerticalOffset = -70f;
    private const int MaxDisplayedNameLength = 14;
    private static GameModeHud? _instance;
    private GameObject _panel = null!;
    private RectTransform _panelRect = null!;
    private TextMeshProUGUI _announcement = null!;
    private TextMeshProUGUI _targetAnnouncement = null!;
    private RectTransform _announcementRect = null!;
    private RectTransform _targetAnnouncementRect = null!;
    private TextMeshProUGUI _objectiveStatus = null!;
    private TextMeshProUGUI _interactionPrompt = null!;
    private TextMeshProUGUI _scorePopup = null!;
    private TextMeshProUGUI _scoreboard = null!;
    private RectTransform _scorePopupRect = null!;
    private CanvasGroup _scorePopupCanvas = null!;
    private Vector2 _scorePopupBasePosition;
    private float _nextRefreshTime;
    private float _announcementUntil;
    private float _targetAnnouncementUntil;
    private float _scorePopupUntil;
    private float _scorePopupStartedAt;
    private bool _hasLoggedVisibility;
    private bool _lastVisible;
    private string _lastVisibilityReason = string.Empty;
    private static int _nextTakeResultId;
    private static int _lastReceivedTakeResultId = -1;
    private static int _pendingTakeResultId = -1;
    private static string _pendingTakeResultText = string.Empty;
    private static float _pendingTakeResultUntil;
    private static float _nextTakeResultPushTime;

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
        _announcementRect = announcementRect;
        announcementRect.anchorMin = new Vector2(0f, 1f);
        announcementRect.anchorMax = new Vector2(0f, 1f);
        announcementRect.pivot = new Vector2(0f, 1f);
        announcementRect.sizeDelta = new Vector2(AnnouncementWidth, 110f);
        announcementRect.anchoredPosition = new Vector2(AnnouncementHorizontalMargin, -320f);
        _announcement = announcementObject.AddComponent<TextMeshProUGUI>();
        _announcement.fontSize = 40f;
        _announcement.fontStyle = FontStyles.Bold;
        _announcement.richText = true;
        _announcement.alignment = TextAlignmentOptions.TopLeft;
        _announcement.enableWordWrapping = true;
        _announcement.outlineWidth = 0.2f;
        _announcement.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        _announcement.raycastTarget = false;
        announcementObject.SetActive(false);

        GameObject targetAnnouncementObject = new("GameModeTargetAnnouncement");
        targetAnnouncementObject.transform.SetParent(transform, false);
        RectTransform targetAnnouncementRect = targetAnnouncementObject.AddComponent<RectTransform>();
        _targetAnnouncementRect = targetAnnouncementRect;
        targetAnnouncementRect.anchorMin = new Vector2(0f, 1f);
        targetAnnouncementRect.anchorMax = new Vector2(0f, 1f);
        targetAnnouncementRect.pivot = new Vector2(0f, 1f);
        targetAnnouncementRect.sizeDelta = new Vector2(AnnouncementWidth, 170f);
        targetAnnouncementRect.anchoredPosition = new Vector2(AnnouncementHorizontalMargin, -446f);
        _targetAnnouncement = targetAnnouncementObject.AddComponent<TextMeshProUGUI>();
        _targetAnnouncement.fontSize = 36f;
        _targetAnnouncement.fontStyle = FontStyles.Bold;
        _targetAnnouncement.richText = true;
        _targetAnnouncement.alignment = TextAlignmentOptions.TopLeft;
        _targetAnnouncement.enableWordWrapping = true;
        _targetAnnouncement.outlineWidth = 0.2f;
        _targetAnnouncement.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        _targetAnnouncement.raycastTarget = false;
        targetAnnouncementObject.SetActive(false);

        GameObject objectiveStatusObject = new("GameModeObjectiveStatus");
        objectiveStatusObject.transform.SetParent(transform, false);
        RectTransform objectiveStatusRect = objectiveStatusObject.AddComponent<RectTransform>();
        objectiveStatusRect.anchorMin = new Vector2(0.5f, 0.5f);
        objectiveStatusRect.anchorMax = new Vector2(0.5f, 0.5f);
        objectiveStatusRect.pivot = new Vector2(0.5f, 0.5f);
        objectiveStatusRect.sizeDelta = new Vector2(900f, 90f);
        objectiveStatusRect.anchoredPosition = new Vector2(0f, -255f);
        _objectiveStatus = objectiveStatusObject.AddComponent<TextMeshProUGUI>();
        _objectiveStatus.fontSize = 32f;
        _objectiveStatus.fontStyle = FontStyles.Bold;
        _objectiveStatus.color = new Color32(255, 211, 74, 255);
        _objectiveStatus.richText = true;
        _objectiveStatus.alignment = TextAlignmentOptions.Center;
        _objectiveStatus.enableWordWrapping = false;
        _objectiveStatus.outlineWidth = 0.25f;
        _objectiveStatus.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        _objectiveStatus.raycastTarget = false;
        objectiveStatusObject.SetActive(false);

        GameObject interactionPromptObject = new("GameModeInteractionPrompt");
        interactionPromptObject.transform.SetParent(transform, false);
        RectTransform interactionPromptRect = interactionPromptObject.AddComponent<RectTransform>();
        interactionPromptRect.anchorMin = new Vector2(0.5f, 0.5f);
        interactionPromptRect.anchorMax = new Vector2(0.5f, 0.5f);
        interactionPromptRect.pivot = new Vector2(0.5f, 0.5f);
        interactionPromptRect.sizeDelta = new Vector2(900f, 90f);
        interactionPromptRect.anchoredPosition = new Vector2(0f, -325f);
        _interactionPrompt = interactionPromptObject.AddComponent<TextMeshProUGUI>();
        _interactionPrompt.fontSize = 32f;
        _interactionPrompt.fontStyle = FontStyles.Bold;
        _interactionPrompt.color = Color.white;
        _interactionPrompt.richText = true;
        _interactionPrompt.alignment = TextAlignmentOptions.Center;
        _interactionPrompt.enableWordWrapping = false;
        _interactionPrompt.outlineWidth = 0.25f;
        _interactionPrompt.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        _interactionPrompt.raycastTarget = false;
        interactionPromptObject.SetActive(false);

        GameObject scorePopupObject = new("GameModeScorePopup");
        scorePopupObject.transform.SetParent(transform, false);
        RectTransform scorePopupRect = scorePopupObject.AddComponent<RectTransform>();
        scorePopupRect.anchorMin = new Vector2(0.5f, 0.5f);
        scorePopupRect.anchorMax = new Vector2(0.5f, 0.5f);
        scorePopupRect.pivot = new Vector2(0.5f, 0.5f);
        scorePopupRect.sizeDelta = new Vector2(400f, 80f);
        scorePopupRect.anchoredPosition = new Vector2(0f, ScorePopupVerticalOffset);
        _scorePopupRect = scorePopupRect;
        _scorePopupBasePosition = scorePopupRect.anchoredPosition;
        _scorePopup = scorePopupObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset? scorePopupFont = ResolveScorePopupFont();
        if (scorePopupFont != null)
        {
            _scorePopup.font = scorePopupFont;
        }
        _scorePopup.fontSize = 48f;
        _scorePopup.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        _scorePopup.color = new Color32(255, 190, 55, 255);
        _scorePopup.richText = true;
        _scorePopup.alignment = TextAlignmentOptions.Center;
        _scorePopup.enableWordWrapping = false;
        _scorePopup.characterSpacing = 2f;
        _scorePopup.outlineWidth = 0.28f;
        _scorePopup.outlineColor = new Color32(16, 18, 24, 255);
        _scorePopup.raycastTarget = false;
        _scorePopupCanvas = scorePopupObject.AddComponent<CanvasGroup>();
        scorePopupObject.SetActive(false);

        _panel = new GameObject("GameModePanel");
        _panel.transform.SetParent(transform, false);
        RectTransform panelRect = _panel.AddComponent<RectTransform>();
        _panelRect = panelRect;
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.sizeDelta = new Vector2(420f, 0f);
        panelRect.anchoredPosition = new Vector2(18f, -148f);
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
        if (_scorePopup.gameObject.activeSelf)
        {
            UpdateScorePopupAnimation();
        }

        PauseManager? pauseManager = PauseManager.Instance;
        bool hideTargetAnnouncement = !GameModeManager.IsCustomMode
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || GameModeManager.IsMatchOver
            || pauseManager?.inMainMenu == true
            || pauseManager?.inVictoryMenu == true;
        if (hideTargetAnnouncement)
        {
            _targetAnnouncement.gameObject.SetActive(false);
        }
        else if (_targetAnnouncement.gameObject.activeSelf
            && Time.unscaledTime >= _targetAnnouncementUntil)
        {
            _targetAnnouncement.gameObject.SetActive(false);
        }

        if (GameModeManager.IsMatchOver)
        {
            DebugLog.Info($"HUD match-over gate scene={SceneManager.GetActiveScene().name} "
                + $"mode={GameModeManager.ActiveMode} phase={GameModeManager.Phase} round={GameModeManager.RoundId}");
            if (!_hasLoggedVisibility || _lastVisible || _lastVisibilityReason != "match-over")
            {
                _hasLoggedVisibility = true;
                _lastVisible = false;
                _lastVisibilityReason = "match-over";
                DebugLog.Info($"[GameModeHud] visible=false reason=match-over "
                    + $"mode={GameModeManager.ActiveMode} phase={GameModeManager.Phase} "
                    + $"round={GameModeManager.RoundId} players=0 scores={GunGameState.Progress.Count}");
            }

            _announcement.gameObject.SetActive(false);
            _targetAnnouncement.gameObject.SetActive(false);
            _objectiveStatus.gameObject.SetActive(false);
            _interactionPrompt.gameObject.SetActive(false);
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
        string activeSceneName = SceneManager.GetActiveScene().name;
        bool inMainMenu = pauseManager?.inMainMenu == true || activeSceneName == "MainMenu";
        bool inVictoryMenu = pauseManager?.inVictoryMenu == true
            || activeSceneName == "VictoryScene" || activeSceneName == "EndGame";
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
        else if (isMatchOver || inVictoryMenu)
        {
            visibilityReason = "match-over";
        }
        else
        {
            connectedPlayerCount = PlayerLookup.GetConnectedPlayerIds().Count;
            bool activeRoundWithPlayers = GameModeManager.Phase == GameModePhase.ActiveRound
                && connectedPlayerCount > 0;
            if (inMainMenu && !activeRoundWithPlayers)
            {
                visibilityReason = "main-menu";
            }
            else if (connectedPlayerCount > 0 || GameModeManager.Phase == GameModePhase.ActiveRound
                || (!inMainMenu && !inVictoryMenu))
            {
                visibilityReason = "visible";
            }
            else
            {
                visibilityReason = "no-connected-players";
            }
        }

        bool visible = visibilityReason == "visible";
        DebugLog.Every("hud-heartbeat", 1f,
            $"HUD state visible={visible} reason={visibilityReason} panel={_panel.activeSelf} "
            + $"mode={GameModeManager.ActiveMode} phase={GameModeManager.Phase} round={GameModeManager.RoundId} "
            + $"players={connectedPlayerCount} scores={GunGameState.Progress.Count} "
            + $"scene={SceneManager.GetActiveScene().name} mainMenu={inMainMenu} "
            + $"victoryMenu={inVictoryMenu} scoreboardLength={_scoreboard.text?.Length ?? 0}");
        if (!_hasLoggedVisibility || visible != _lastVisible || visibilityReason != _lastVisibilityReason)
        {
            _hasLoggedVisibility = true;
            _lastVisible = visible;
            _lastVisibilityReason = visibilityReason;
            DebugLog.Info($"[GameModeHud] visible={visible} reason={visibilityReason} "
                + $"mode={GameModeManager.ActiveMode} phase={GameModeManager.Phase} "
                + $"round={GameModeManager.RoundId} players={connectedPlayerCount} "
                + $"scores={GunGameState.Progress.Count}");
        }

        _panel.SetActive(visible);
        if (visible)
        {
            RefreshScoreboard();
        }
        UpdateAnnouncementLayout();

        string objectiveStatus = visible && GameModeManager.IsActive(GameMode.SearchAndDestroy)
            ? SearchAndDestroyState.GetLocalBombStatusText()
            : string.Empty;
        _objectiveStatus.text = objectiveStatus;
        _objectiveStatus.gameObject.SetActive(objectiveStatus.Length > 0);

        string interactionPrompt = visible && GameModeManager.IsActive(GameMode.SearchAndDestroy)
            ? SearchAndDestroyState.GetLocalInteractionPrompt()
            : string.Empty;
        _interactionPrompt.text = interactionPrompt;
        _interactionPrompt.gameObject.SetActive(interactionPrompt.Length > 0);

        if (visible && GameModeManager.IsActive(GameMode.OneInTheChamber))
        {
            string countdown = OneInTheChamberState.GetLoadoutCountdownText();
            if (countdown.Length > 0)
            {
                _targetAnnouncement.text = countdown;
                _targetAnnouncementUntil = Time.unscaledTime + RefreshInterval + 0.1f;
                _targetAnnouncement.gameObject.SetActive(true);
            }
        }
    }

    internal static void AnnounceActiveMode()
    {
        if (_instance == null || !GameModeManager.IsCustomMode || GameModeManager.IsMatchOver)
        {
            return;
        }

        _instance.UpdateAnnouncementLayout();
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
        PauseManager? pauseManager = PauseManager.Instance;
        if (_instance == null || !GameModeManager.IsCustomMode
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || GameModeManager.IsMatchOver || pauseManager?.inMainMenu == true
            || pauseManager?.inVictoryMenu == true)
        {
            return;
        }

        _instance.UpdateAnnouncementLayout();
        _instance._targetAnnouncement.text = text;
        _instance._targetAnnouncementUntil = Time.unscaledTime + Mathf.Max(0f, durationSeconds);
        _instance._targetAnnouncement.gameObject.SetActive(true);
    }

    internal static void ReceiveTakeResult(int resultId, string text, float durationSeconds)
    {
        if (resultId <= _lastReceivedTakeResultId)
        {
            return;
        }

        _lastReceivedTakeResultId = resultId;
        ShowTakeResult(ClientInstance.ReplaceAllPlayerNameTags(text), durationSeconds);
    }

    internal static void PeriodicPushTakeResult()
    {
        if (!MyceliumNetwork.IsHost || _pendingTakeResultId < 0
            || Time.unscaledTime >= _pendingTakeResultUntil
            || Time.unscaledTime < _nextTakeResultPushTime)
        {
            return;
        }

        _nextTakeResultPushTime = Time.unscaledTime + 0.5f;
        MyceliumNetwork.RPC(GameModeManager.ModId, nameof(Plugin.SyncTakeResult),
            ReliableType.Reliable, _pendingTakeResultId, _pendingTakeResultText,
            _pendingTakeResultUntil - Time.unscaledTime);
    }

    internal static void BroadcastTakeResult(string text, float durationSeconds = 3f)
    {
        if (string.IsNullOrWhiteSpace(text) || !MyceliumNetwork.InLobby
            || !MyceliumNetwork.IsHost)
        {
            return;
        }

        int resultId = ++_nextTakeResultId;
        _pendingTakeResultId = resultId;
        _pendingTakeResultText = text;
        _pendingTakeResultUntil = Time.unscaledTime + Mathf.Max(1f, durationSeconds);
        _nextTakeResultPushTime = Time.unscaledTime + 0.5f;
        string resolvedText = ClientInstance.ReplaceAllPlayerNameTags(text);
        ShowTakeResult(resolvedText, durationSeconds);
        MyceliumNetwork.RPC(GameModeManager.ModId, nameof(Plugin.SyncTakeResult),
            ReliableType.Reliable, resultId, text, durationSeconds);
    }

    internal static void ShowTakeResult(string text, float durationSeconds)
    {
        PauseManager? pauseManager = PauseManager.Instance;
        if (_instance == null || string.IsNullOrWhiteSpace(text)
            || !GameModeManager.IsCustomMode || GameModeManager.IsMatchOver
            || pauseManager?.inMainMenu == true || pauseManager?.inVictoryMenu == true)
        {
            return;
        }

        _instance.UpdateAnnouncementLayout();
        _instance._targetAnnouncement.text = text;
        _instance._targetAnnouncementUntil = Time.unscaledTime + Mathf.Max(0f, durationSeconds);
        _instance._targetAnnouncement.gameObject.SetActive(true);
    }

    private void UpdateAnnouncementLayout()
    {
        if (_panelRect == null || _announcementRect == null || _targetAnnouncementRect == null)
        {
            return;
        }

        float scoreboardHeight = _panel != null && _panel.activeSelf ? _panelRect.rect.height : 0f;
        float announcementTop = _panelRect.anchoredPosition.y - scoreboardHeight - AnnouncementGap;
        _announcementRect.anchoredPosition = new Vector2(AnnouncementHorizontalMargin,
            announcementTop);
        _targetAnnouncementRect.anchoredPosition = new Vector2(AnnouncementHorizontalMargin,
            announcementTop - _announcementRect.sizeDelta.y - AnnouncementGap);
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
        _instance._scorePopupStartedAt = Time.unscaledTime;
        _instance._scorePopupUntil = _instance._scorePopupStartedAt + ScorePopupDuration;
        _instance._scorePopupRect.localScale = Vector3.one * ScorePopupStartScale;
        _instance._scorePopupRect.anchoredPosition = _instance._scorePopupBasePosition;
        _instance._scorePopupCanvas.alpha = 1f;
        _instance._scorePopup.gameObject.SetActive(true);
    }

    private void UpdateScorePopupAnimation()
    {
        float now = Time.unscaledTime;
        if (now >= _scorePopupUntil)
        {
            _scorePopup.gameObject.SetActive(false);
            _scorePopupRect.localScale = Vector3.one;
            _scorePopupRect.anchoredPosition = _scorePopupBasePosition;
            _scorePopupCanvas.alpha = 1f;
            return;
        }

        float elapsed = now - _scorePopupStartedAt;
        float scale;
        float alpha;
        float rise;
        if (elapsed < ScorePopupImpactDuration)
        {
            float impactProgress = Mathf.Clamp01(elapsed / ScorePopupImpactDuration);
            float easedImpact = 1f - Mathf.Pow(1f - impactProgress, 3f);
            scale = Mathf.Lerp(ScorePopupStartScale, 1f, easedImpact);
            alpha = 1f;
            rise = 0f;
        }
        else
        {
            float shrinkProgress = Mathf.Clamp01((elapsed - ScorePopupImpactDuration)
                / (ScorePopupDuration - ScorePopupImpactDuration));
            float easedShrink = Mathf.SmoothStep(0f, 1f, shrinkProgress);
            scale = Mathf.Lerp(1f, ScorePopupEndScale, easedShrink);
            alpha = 1f - easedShrink;
            rise = ScorePopupRise * easedShrink;
        }

        _scorePopupRect.localScale = Vector3.one * scale;
        _scorePopupRect.anchoredPosition = _scorePopupBasePosition + Vector2.up * rise;
        _scorePopupCanvas.alpha = alpha;
    }

    private static TMP_FontAsset? ResolveScorePopupFont()
    {
        string[] resourceNames =
        {
            "Fonts & Materials/Anton SDF",
            "Fonts & Materials/Bebas Neue SDF",
            "Fonts & Materials/Oswald SDF",
            "Fonts & Materials/RobotoCondensed SDF"
        };
        foreach (string resourceName in resourceNames)
        {
            TMP_FontAsset? font = Resources.Load<TMP_FontAsset>(resourceName);
            if (font != null)
            {
                return font;
            }
        }

        TMP_FontAsset? defaultFont = TMP_Settings.defaultFontAsset;
        foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font == defaultFont)
            {
                continue;
            }

            string fontName = font.name;
            if (fontName.IndexOf("Anton", StringComparison.OrdinalIgnoreCase) >= 0
                || fontName.IndexOf("Bebas", StringComparison.OrdinalIgnoreCase) >= 0
                || fontName.IndexOf("Oswald", StringComparison.OrdinalIgnoreCase) >= 0
                || fontName.IndexOf("RobotoCondensed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return font;
            }
        }

        return defaultFont;
    }

    private void RefreshScoreboard()
    {
        if (GameModeManager.IsActive(GameMode.CaptureTheFlag))
        {
            _scoreboard.text = CaptureTheFlagHud.BuildScoreboard();
            return;
        }

        if (GameModeManager.IsActive(GameMode.SearchAndDestroy))
        {
            _scoreboard.text = SearchAndDestroyHud.BuildScoreboard();
            return;
        }

        if (GameModeManager.IsActive(GameMode.TeamDeathmatch))
        {
            _scoreboard.text = TeamDeathmatchHud.BuildScoreboard();
            return;
        }

        if (GameModeManager.IsActive(GameMode.Hardpoint))
        {
            _scoreboard.text = HardpointHud.BuildScoreboard();
            return;
        }

        if (GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            GameModeScoreboardRow[] survivorRows =
            {
                new GameModeScoreboardRow("Survivors", MichaelMeyersState.SurvivorCount)
            };
            _scoreboard.text = GameModeScoreboard.Build(GameMode.MichaelMeyers, null, null,
                survivorRows);
            return;
        }
        Dictionary<int, int> scores;
        bool crownFirst;
        int pointsToWin;
        if (GameModeManager.IsActive(GameMode.FreeForAll))
        {
            pointsToWin = FFAState.KillsToWin;
            scores = FFAState.Kills;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.Juggernaut))
        {
            pointsToWin = JuggernautState.PointsToWin;
            scores = JuggernautState.Points;
            crownFirst = true;
        }
        else if (GameModeManager.IsActive(GameMode.HVT))
        {
            pointsToWin = HVTState.PointsToWin;
            scores = HVTState.Points;
            crownFirst = true;
        }
        else if (GameModeManager.IsActive(GameMode.SniperBattle))
        {
            pointsToWin = SniperBattleState.PointsToWin;
            scores = SniperBattleState.Points;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.KillTheRat))
        {
            pointsToWin = KillTheRatState.PointsToWin;
            scores = KillTheRatState.Points;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.HotPotato))
        {
            pointsToWin = HotPotatoState.KillsToWin;
            scores = HotPotatoState.Kills;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.Infidel))
        {
            pointsToWin = InfidelState.KillsToWin;
            scores = InfidelState.Scores;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.Assassin))
        {
            pointsToWin = AssassinState.PointsToWin;
            scores = AssassinState.Scores;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.OneInTheChamber))
        {
            pointsToWin = OneInTheChamberState.PointsToWin;
            scores = OneInTheChamberState.Scores;
            crownFirst = false;
        }
        else if (GameModeManager.IsActive(GameMode.Default))
        {
            pointsToWin = DefaultGameModeState.PointsToWin;
            scores = DefaultGameModeState.Scores;
            crownFirst = false;
        }
        else
        {
            pointsToWin = GunGameState.ScoreLimit;
            scores = GunGameState.Progress;
            crownFirst = false;
        }

        List<int> playerIds = PlayerLookup.GetConnectedPlayerIds();
        int crownPlayerId = GameModeManager.IsActive(GameMode.Juggernaut)
            ? JuggernautState.CurrentJuggernautPlayerId
            : HVTState.CurrentHVTPlayerId;
        bool hvtCrown = GameModeManager.IsActive(GameMode.HVT);
        playerIds.Sort((left, right) =>
        {
            bool leftIsCrown = crownFirst && left == crownPlayerId;
            bool rightIsCrown = crownFirst && right == crownPlayerId;
            if (leftIsCrown != rightIsCrown)
            {
                return leftIsCrown ? -1 : 1;
            }

            scores.TryGetValue(left, out int leftScore);
            scores.TryGetValue(right, out int rightScore);
            return rightScore.CompareTo(leftScore);
        });

        List<GameModeScoreboardRow> rows = new(playerIds.Count);
        foreach (int playerId in playerIds)
        {
            scores.TryGetValue(playerId, out int score);
            string playerName = ClientInstance.ReplaceAllPlayerNameTags(PlayerLookup.GetPlayerNameTag(playerId));
            if (playerName.Length > MaxDisplayedNameLength)
            {
                playerName = playerName.Substring(0, MaxDisplayedNameLength);
            }
            playerName = GameModeScoreboard.ApplyPlayerNameGradient(playerName);

            bool isCrown = crownFirst && playerId == crownPlayerId;
            if (isCrown)
            {
                playerName += hvtCrown ? "  HVT" : "  JUG";
            }
            rows.Add(new GameModeScoreboardRow(playerName, score, playerId: playerId));
        }

        _scoreboard.text = GameModeScoreboard.Build(GameModeManager.ActiveMode, null,
            pointsToWin, rows);
    }
}