using System;
using System.Collections.Generic;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace StraftatEightsPlugin;

internal sealed class GameModeHud : MonoBehaviour
{
    private const float RefreshInterval = 0.1f;
    private const float AnnouncementDuration = 2f;
    private const float AnnouncementTopMargin = 0.12f;
    private const float AnnouncementWidth = 600f;
    private const float AnnouncementGap = 8f;
    private const float ScorePopupDuration = 0.65f;
    private const float ScorePopupImpactDuration = 0.12f;
    private const float ScorePopupStartScale = 1.18f;
    private const float ScorePopupEndScale = 0.72f;
    private const float ScorePopupRise = 18f;
    private const float ScorePopupVerticalOffset = -70f;
    private const float TakeResultRetryInterval = 0.5f;
    private const int MaxPendingTakeResults = 8;
    private const int MaxReceivedTakeResults = 32;
    private const int MaxDisplayedNameLength = 14;
    private const int PanelTextureSize = 64;
    private const int PanelCornerRadius = 12;
    private const int PanelBorderWidth = 2;
    private static readonly Color32 ScoreboardTextColor = new(225, 238, 220, 255);
    private static readonly Color32 ScoreboardGlowColor = new(92, 105, 84, 180);
    private static readonly Color32 ScoreboardPanelBorderColor = new(138, 116, 58, 230);
    private static readonly Color32 ScoreboardPanelFillColor = new(28, 35, 33, 248);
    private sealed class PendingTakeResult
    {
        internal int ResultId;
        internal string Text = string.Empty;
        internal float ExpiresAt;
    }

    private static GameModeHud? _instance;
    private static TMP_FontAsset? _pluginFont;
    private static bool _fontFallbackLogged;
    private static Sprite? _scoreboardPanelSprite;
    private static Texture2D? _scoreboardPanelTexture;
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
    private TextMeshProUGUI _respawnProtectionMarker = null!;
    private RectTransform _scorePopupRect = null!;
    private CanvasGroup _scorePopupCanvas = null!;
    private Vector2 _scorePopupBasePosition;
    private float _nextRefreshTime;
    private float _announcementUntil;
    private float _targetAnnouncementUntil;
    private bool _targetAnnouncementAllowsEndingRound;
    private float _scorePopupUntil;
    private float _scorePopupStartedAt;
    private bool _hasLoggedVisibility;
    private bool _lastVisible;
    private string _lastVisibilityReason = string.Empty;
    private static int _nextTakeResultId;
    private static readonly List<PendingTakeResult> PendingTakeResults = new();
    private static readonly HashSet<int> ReceivedTakeResultIds = new();
    private static readonly Queue<int> ReceivedTakeResultOrder = new();
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
        announcementRect.anchorMin = new Vector2(0.5f, 1f);
        announcementRect.anchorMax = new Vector2(0.5f, 1f);
        announcementRect.pivot = new Vector2(0.5f, 1f);
        announcementRect.sizeDelta = new Vector2(AnnouncementWidth, 110f);
        announcementRect.anchoredPosition = new Vector2(0f, -129.6f);
        _announcement = announcementObject.AddComponent<TextMeshProUGUI>();
        ApplyPluginFont(_announcement);
        _announcement.fontSize = 40f;
        _announcement.fontStyle = FontStyles.Bold;
        _announcement.richText = true;
        _announcement.alignment = TextAlignmentOptions.Top;
        _announcement.enableWordWrapping = true;
        _announcement.outlineWidth = 0.2f;
        _announcement.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        _announcement.raycastTarget = false;
        announcementObject.SetActive(false);

        GameObject targetAnnouncementObject = new("GameModeTargetAnnouncement");
        targetAnnouncementObject.transform.SetParent(transform, false);
        RectTransform targetAnnouncementRect = targetAnnouncementObject.AddComponent<RectTransform>();
        _targetAnnouncementRect = targetAnnouncementRect;
        targetAnnouncementRect.anchorMin = new Vector2(0.5f, 1f);
        targetAnnouncementRect.anchorMax = new Vector2(0.5f, 1f);
        targetAnnouncementRect.pivot = new Vector2(0.5f, 1f);
        targetAnnouncementRect.sizeDelta = new Vector2(AnnouncementWidth, 170f);
        targetAnnouncementRect.anchoredPosition = new Vector2(0f, -129.6f);
        _targetAnnouncement = targetAnnouncementObject.AddComponent<TextMeshProUGUI>();
        ApplyPluginFont(_targetAnnouncement);
        _targetAnnouncement.fontSize = 36f;
        _targetAnnouncement.fontStyle = FontStyles.Bold;
        _targetAnnouncement.richText = true;
        _targetAnnouncement.alignment = TextAlignmentOptions.Top;
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
        ApplyPluginFont(_objectiveStatus);
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
        ApplyPluginFont(_interactionPrompt);
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
        ApplyPluginFont(_scorePopup);
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
        Image panelImage = _panel.AddComponent<Image>();
        panelImage.sprite = GetScoreboardPanelSprite();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = Color.white;
        Shadow panelShadow = _panel.AddComponent<Shadow>();
        panelShadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
        panelShadow.effectDistance = new Vector2(3f, -3f);
        panelShadow.useGraphicAlpha = true;
        Outline panelOutline = _panel.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.72f, 0.58f, 0.24f, 0.3f);
        panelOutline.effectDistance = new Vector2(1f, 1f);
        panelOutline.useGraphicAlpha = true;

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
        ApplyPluginFont(_scoreboard);
        _scoreboard.fontSize = 22f;
        _scoreboard.color = ScoreboardTextColor;
        _scoreboard.richText = true;
        _scoreboard.enableWordWrapping = false;
        _scoreboard.alignment = TextAlignmentOptions.TopLeft;
        _scoreboard.outlineWidth = 0.16f;
        _scoreboard.outlineColor = ScoreboardGlowColor;
        _scoreboard.raycastTarget = false;

        GameObject respawnProtectionObject = new("RespawnProtectionCursor");
        respawnProtectionObject.transform.SetParent(transform, false);
        RectTransform respawnProtectionRect = respawnProtectionObject.AddComponent<RectTransform>();
        respawnProtectionRect.anchorMin = new Vector2(0.5f, 0.5f);
        respawnProtectionRect.anchorMax = new Vector2(0.5f, 0.5f);
        respawnProtectionRect.pivot = new Vector2(0.5f, 0.5f);
        respawnProtectionRect.sizeDelta = new Vector2(64f, 64f);
        _respawnProtectionMarker = respawnProtectionObject.AddComponent<TextMeshProUGUI>();
        ApplyPluginFont(_respawnProtectionMarker);
        _respawnProtectionMarker.text = "X";
        _respawnProtectionMarker.fontSize = 34f;
        _respawnProtectionMarker.fontStyle = FontStyles.Bold;
        _respawnProtectionMarker.color = new Color32(255, 48, 48, 255);
        _respawnProtectionMarker.alignment = TextAlignmentOptions.Center;
        _respawnProtectionMarker.enableWordWrapping = false;
        _respawnProtectionMarker.outlineWidth = 0.25f;
        _respawnProtectionMarker.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        _respawnProtectionMarker.raycastTarget = false;
        respawnProtectionObject.SetActive(false);
        _panel.SetActive(false);
    }

    private void Update()
    {
        if (_scorePopup.gameObject.activeSelf)
        {
            UpdateScorePopupAnimation();
        }

        PauseManager? pauseManager = PauseManager.Instance;
        bool showRespawnProtectionMarker = RespawnProtection.IsLocalPlayerProtected()
            && !GameModeManager.IsMatchOver
            && pauseManager?.inMainMenu != true
            && pauseManager?.inVictoryMenu != true;
        _respawnProtectionMarker.gameObject.SetActive(showRespawnProtectionMarker);
        bool targetAnnouncementCanContinueAfterRound = GameModeManager.Phase == GameModePhase.EndingRound
            && _targetAnnouncementAllowsEndingRound;
        bool hideTargetAnnouncement = !GameModeManager.IsCustomMode
            || (GameModeManager.Phase != GameModePhase.ActiveRound
                && !targetAnnouncementCanContinueAfterRound)
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
        _instance._targetAnnouncementAllowsEndingRound = false;
        _instance._targetAnnouncementUntil = Time.unscaledTime + Mathf.Max(0f, durationSeconds);
        _instance._targetAnnouncement.gameObject.SetActive(true);
    }

    internal static void BroadcastAnnouncement(string text, float durationSeconds = AnnouncementDuration)
    {
        if (string.IsNullOrWhiteSpace(text) || !MyceliumNetwork.InLobby
            || !MyceliumNetwork.IsHost)
        {
            return;
        }

        PublishAnnouncement(text, Mathf.Clamp(durationSeconds, 1f, 8f), showHud: true);
    }

    internal static void ReceiveTakeResult(int resultId, string text, float durationSeconds)
    {
        if (resultId < 0 || string.IsNullOrWhiteSpace(text)
            || !RememberTakeResult(resultId))
        {
            return;
        }

        ShowTakeResult(ClientInstance.ReplaceAllPlayerNameTags(text), durationSeconds);
    }

    internal static void PeriodicPushTakeResult()
    {
        if (!MyceliumNetwork.IsHost || PendingTakeResults.Count == 0
            || Time.unscaledTime < _nextTakeResultPushTime)
        {
            return;
        }

        float now = Time.unscaledTime;
        for (int index = PendingTakeResults.Count - 1; index >= 0; index--)
        {
            if (PendingTakeResults[index].ExpiresAt <= now)
            {
                PendingTakeResults.RemoveAt(index);
            }
        }

        if (PendingTakeResults.Count == 0)
        {
            return;
        }

        _nextTakeResultPushTime = now + TakeResultRetryInterval;
        foreach (PendingTakeResult result in PendingTakeResults)
        {
            MyceliumNetwork.RPC(GameModeManager.ModId, nameof(Plugin.SyncTakeResult),
                ReliableType.Reliable, result.ResultId, result.Text,
                Mathf.Max(0f, result.ExpiresAt - now));
        }
    }

    internal static void BroadcastTakeResult(string text, float durationSeconds = 3f)
    {
        if (string.IsNullOrWhiteSpace(text) || !MyceliumNetwork.InLobby
            || !MyceliumNetwork.IsHost)
        {
            return;
        }

        int resultId = ++_nextTakeResultId;
        float effectiveDuration = Mathf.Clamp(durationSeconds, 1f, 8f);
        PendingTakeResults.Add(new PendingTakeResult
        {
            ResultId = resultId,
            Text = text,
            ExpiresAt = Time.unscaledTime + effectiveDuration
        });
        if (PendingTakeResults.Count > MaxPendingTakeResults)
        {
            PendingTakeResults.RemoveAt(0);
        }

        _nextTakeResultPushTime = Time.unscaledTime + TakeResultRetryInterval;
        PublishAnnouncement(text, effectiveDuration, showHud: false);
        ShowTakeResult(ClientInstance.ReplaceAllPlayerNameTags(text), effectiveDuration);
        MyceliumNetwork.RPC(GameModeManager.ModId, nameof(Plugin.SyncTakeResult),
            ReliableType.Reliable, resultId, text, effectiveDuration);
    }

    internal static void ReceiveAnnouncement(string text, float durationSeconds, bool showHud)
    {
        if (string.IsNullOrWhiteSpace(text)
            || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
        {
            return;
        }

        string displayText = ClientInstance.ReplaceAllPlayerNameTags(text);
        WriteKillfeedAnnouncement(displayText);
        if (showHud)
        {
            AnnounceTarget(displayText, Mathf.Clamp(durationSeconds, 1f, 8f));
        }
    }

    private static void PublishAnnouncement(string text, float durationSeconds, bool showHud)
    {
        string displayText = ClientInstance.ReplaceAllPlayerNameTags(text);
        WriteKillfeedAnnouncement(displayText);
        if (showHud)
        {
            AnnounceTarget(displayText, durationSeconds);
        }

        MyceliumNetwork.RPC(GameModeManager.ModId, nameof(Plugin.SyncGameModeAnnouncement),
            ReliableType.Reliable, text, durationSeconds, showHud);
    }

    private static void WriteKillfeedAnnouncement(string text)
    {
        if (MatchLogs.Instance != null)
        {
            MatchLogs.Instance.WriteLocalLog(text);
        }
    }

    private static bool RememberTakeResult(int resultId)
    {
        if (!ReceivedTakeResultIds.Add(resultId))
        {
            return false;
        }

        ReceivedTakeResultOrder.Enqueue(resultId);
        while (ReceivedTakeResultOrder.Count > MaxReceivedTakeResults)
        {
            ReceivedTakeResultIds.Remove(ReceivedTakeResultOrder.Dequeue());
        }

        return true;
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
        _instance._targetAnnouncementAllowsEndingRound = true;
        _instance._targetAnnouncementUntil = Time.unscaledTime + Mathf.Max(0f, durationSeconds);
        _instance._targetAnnouncement.gameObject.SetActive(true);
    }

    private void UpdateAnnouncementLayout()
    {
        if (_panelRect == null || _announcementRect == null || _targetAnnouncementRect == null)
        {
            return;
        }

        if (_panel != null && _panel.activeSelf)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);
        }

        RectTransform? canvasRect = transform as RectTransform;
        float canvasHeight = canvasRect != null && canvasRect.rect.height > 0f
            ? canvasRect.rect.height
            : 1080f;
        float announcementTop = -canvasHeight * AnnouncementTopMargin;
        _announcementRect.anchoredPosition = new Vector2(0f, announcementTop);
        float targetAnnouncementTop = _announcement.gameObject.activeSelf
            ? announcementTop - _announcementRect.sizeDelta.y - AnnouncementGap
            : announcementTop;
        _targetAnnouncementRect.anchoredPosition = new Vector2(0f, targetAnnouncementTop);
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

    private static void ApplyPluginFont(TextMeshProUGUI text)
    {
        TMP_FontAsset? font = ResolvePluginFont();
        if (font != null)
        {
            text.font = font;
        }
    }

    private static TMP_FontAsset? ResolvePluginFont()
    {
        if (_pluginFont != null)
        {
            return _pluginFont;
        }

        foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font.name.IndexOf("MajorMonoDisplay", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _pluginFont = font;
                return _pluginFont;
            }
        }

        string[] resourceNames =
        {
            "MajorMonoDisplay-Regular",
            "Fonts/MajorMonoDisplay-Regular"
        };
        foreach (string resourceName in resourceNames)
        {
            UnityEngine.Font? font = Resources.Load<UnityEngine.Font>(resourceName);
            if (font == null)
            {
                continue;
            }

            _pluginFont = CreateDynamicPluginFont(font);
            if (_pluginFont != null)
            {
                return _pluginFont;
            }
        }

        foreach (UnityEngine.Font font in Resources.FindObjectsOfTypeAll<UnityEngine.Font>())
        {
            if (font.name.IndexOf("MajorMonoDisplay", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            _pluginFont = CreateDynamicPluginFont(font);
            if (_pluginFont != null)
            {
                return _pluginFont;
            }
        }

        _pluginFont = TMP_Settings.defaultFontAsset;
        if (!_fontFallbackLogged)
        {
            _fontFallbackLogged = true;
            Plugin.Logger.LogWarning("[GameModeHud] Major Mono Display was not available; "
                + "using the default TMP font.");
        }

        return _pluginFont;
    }

    private static TMP_FontAsset? CreateDynamicPluginFont(UnityEngine.Font font)
    {
        try
        {
            TMP_FontAsset? fontAsset = TMP_FontAsset.CreateFontAsset(font, 90, 9,
                GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (fontAsset != null)
            {
                fontAsset.name = "StraftatEights Major Mono Display";
            }
            return fontAsset;
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogWarning("[GameModeHud] Could not create Major Mono Display TMP asset: "
                + exception.GetBaseException().Message);
            return null;
        }
    }

    private static Sprite GetScoreboardPanelSprite()
    {
        if (_scoreboardPanelSprite != null)
        {
            return _scoreboardPanelSprite;
        }

        _scoreboardPanelTexture = new Texture2D(PanelTextureSize, PanelTextureSize,
            TextureFormat.RGBA32, false)
        {
            name = "StraftatEights Scoreboard Panel",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };

        Color32[] pixels = new Color32[PanelTextureSize * PanelTextureSize];
        Color32 transparent = new(0, 0, 0, 0);
        int innerRadius = PanelCornerRadius - PanelBorderWidth;
        for (int y = 0; y < PanelTextureSize; y++)
        {
            for (int x = 0; x < PanelTextureSize; x++)
            {
                bool insideOuter = IsInsideRoundedRect(x, y, PanelCornerRadius);
                bool insideInner = IsInsideRoundedRect(x, y, innerRadius,
                    PanelBorderWidth);
                pixels[y * PanelTextureSize + x] = !insideOuter
                    ? transparent
                    : insideInner ? ScoreboardPanelFillColor : ScoreboardPanelBorderColor;
            }
        }

        _scoreboardPanelTexture.SetPixels32(pixels);
        _scoreboardPanelTexture.Apply(false, true);
        _scoreboardPanelSprite = Sprite.Create(_scoreboardPanelTexture,
            new Rect(0f, 0f, PanelTextureSize, PanelTextureSize),
            new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect,
            new Vector4(PanelCornerRadius, PanelCornerRadius,
                PanelCornerRadius, PanelCornerRadius));
        _scoreboardPanelSprite.name = "StraftatEights Scoreboard Panel";
        _scoreboardPanelSprite.hideFlags = HideFlags.DontSave;
        return _scoreboardPanelSprite;
    }

    private static bool IsInsideRoundedRect(int x, int y, int radius, int inset = 0)
    {
        int left = inset;
        int right = PanelTextureSize - inset - 1;
        int bottom = inset;
        int top = PanelTextureSize - inset - 1;
        int nearestX = Mathf.Clamp(x, left + radius, right - radius);
        int nearestY = Mathf.Clamp(y, bottom + radius, top - radius);
        int deltaX = x - nearestX;
        int deltaY = y - nearestY;
        return deltaX * deltaX + deltaY * deltaY <= radius * radius;
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
                survivorRows, MichaelMeyersState.TimeRemaining > 0f
                    ? "Timer: " + Mathf.CeilToInt(MichaelMeyersState.TimeRemaining) + "s"
                    : string.Empty);
            return;
        }
        Dictionary<int, int> scores;
        bool crownFirst;
        int pointsToWin;
        string? timerText = null;
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
            timerText = InfidelState.TakeTimeRemaining > 0f
                ? "Timer: " + Mathf.CeilToInt(InfidelState.TakeTimeRemaining) + "s"
                : string.Empty;
        }
        else if (GameModeManager.IsActive(GameMode.Assassin))
        {
            pointsToWin = AssassinState.PointsToWin;
            scores = AssassinState.Scores;
            crownFirst = false;
            timerText = AssassinState.TakeTimeRemaining > 0f
                ? "Take: " + Mathf.CeilToInt(AssassinState.TakeTimeRemaining) + "s"
                : string.Empty;
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

        string modeTimerText = ModeTimeoutState.GetScoreboardText();
        if (modeTimerText.Length > 0)
        {
            string roundTimerText = modeTimerText.StartsWith("Timer: ", StringComparison.Ordinal)
                ? modeTimerText.Substring("Timer: ".Length)
                : modeTimerText;
            timerText = string.IsNullOrEmpty(timerText)
                ? modeTimerText
                : timerText + " | Round: " + roundTimerText;
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
            string playerName = PlayerNameMarkup.Truncate(
                PlayerNameSync.GetDisplayName(playerId), MaxDisplayedNameLength);

            bool isCrown = crownFirst && playerId == crownPlayerId;
            if (isCrown)
            {
                playerName += hvtCrown ? "  HVT" : "  JUG";
            }
            rows.Add(new GameModeScoreboardRow(playerName, score, playerId: playerId));
        }

        _scoreboard.text = GameModeScoreboard.Build(GameModeManager.ActiveMode, null,
            pointsToWin, rows, timerText);
    }
}