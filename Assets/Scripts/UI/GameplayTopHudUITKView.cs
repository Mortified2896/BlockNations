using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class GameplayTopHudUITKView : MonoBehaviour
{
    private const string ThemeResourceName = "GameplayTopHud_UITK_Theme";
    private const float TournamentStandingsDesktopWidthThreshold = 1200f;
    private const string FirstTurnSubmitFailedMessage = "First turn was not submitted. This game was not created on the server yet.";
    private const string FirstTurnSubmitUnauthorizedMessage = "First turn was not submitted because PBp server authentication is missing or invalid.";

    [Header("Spike Toggle")]
    [SerializeField] private bool enableGameplayTopHudUITK = true;

    [Header("Optional Source Overrides")]
    [SerializeField] private TurnManager turnManager;
    private TurnManager subscribedTurnManager;

    private UIDocument uiDocument;
    private ThemeStyleSheet themeAsset;
    private readonly UITKResponsiveSizeTierController responsiveSizeTierController = new UITKResponsiveSizeTierController();

    private VisualElement root;
    private VisualElement hudRoot;
    private VisualElement tournamentStandingsPanel;
    private VisualElement tournamentStandingsRows;
    private Label tournamentStandingsTitleLabel;
    private Label tournamentStandingsRankHeaderLabel;
    private Label tournamentStandingsScoreHeaderLabel;
    private Label turnLabel;
    private Label goldLabel;
    private Label statusLabel;
    private bool uiReady;
    private bool warnedMissingPanelSettings;
    private bool warnedMissingLabels;
    private StyleLength defaultTurnLabelWidth;
    private StyleLength defaultGoldLabelWidth;
    private StyleLength defaultStatusLabelWidth;
    private string pbpSubmitStatusOverrideMessage;
    private string pbpSubmitStatusOverrideGameId;

    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        if (themeAsset == null)
        {
            themeAsset = Resources.Load<ThemeStyleSheet>(ThemeResourceName);
        }
    }

    private void OnEnable()
    {
        ResolveSceneReferences(force: true);
        TrySubscribeToTurnManagerEvents();
        CacheUiElements(force: true);
    }

    private void OnDisable()
    {
        UnsubscribeFromTurnManagerEvents();
        ClearUiCache();
    }

    private void Update()
    {
        if (!enableGameplayTopHudUITK)
        {
            DisableOverlay();
            return;
        }

        if (!ResolveSceneReferences(force: false))
        {
            DisableOverlay();
            return;
        }

        if (!ShouldShowForMode(turnManager.currentMode))
        {
            DisableOverlay();
            return;
        }

        if (!EnsureUiReady())
        {
            return;
        }

        RefreshLabels();
        ApplySafeArea(force: false);
        responsiveSizeTierController.Apply(root);
    }

    private bool ResolveSceneReferences(bool force)
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (turnManager == null || force)
        {
            turnManager = TurnManager.Instance;
            if (turnManager == null)
            {
                turnManager = UnityEngine.Object.FindFirstObjectByType<TurnManager>();
            }
        }

        if (turnManager == null)
        {
            return false;
        }

        TrySubscribeToTurnManagerEvents();
        return uiDocument != null && turnManager != null;
    }

    private static bool ShouldShowForMode(TurnManager.GameMode mode)
    {
        return mode == TurnManager.GameMode.None ||
               mode == TurnManager.GameMode.VsAI ||
               mode == TurnManager.GameMode.PlayByPost;
    }

    private bool EnsureUiReady()
    {
        if (uiDocument == null)
        {
            return false;
        }

        if (!uiDocument.enabled)
        {
            uiDocument.enabled = true;
            uiReady = false;
        }

        if (uiDocument.panelSettings == null)
        {
            if (!warnedMissingPanelSettings)
            {
                warnedMissingPanelSettings = true;
                Debug.LogWarning("GameplayTopHudUITKView: UIDocument requires a PanelSettings asset assigned in scene.", this);
            }

            return false;
        }

        if (uiDocument.panelSettings.themeStyleSheet == null && themeAsset != null)
        {
            uiDocument.panelSettings.themeStyleSheet = themeAsset;
        }

        warnedMissingPanelSettings = false;
        return CacheUiElements(force: false);
    }

    private bool CacheUiElements(bool force)
    {
        if (uiDocument == null)
        {
            return false;
        }

        VisualElement currentRoot = uiDocument.rootVisualElement;
        if (currentRoot == null)
        {
            return false;
        }

        if (!force && uiReady && root == currentRoot)
        {
            return true;
        }

        root = currentRoot;
        hudRoot = root.Q<VisualElement>("GameplayTopHudRoot") ?? root;
        tournamentStandingsPanel = root.Q<VisualElement>("TournamentStandingsPanel");
        tournamentStandingsRows = root.Q<VisualElement>("TournamentStandingsRows");
        tournamentStandingsTitleLabel = root.Q<Label>("TournamentStandingsTitle");
        tournamentStandingsRankHeaderLabel = root.Q<Label>("TournamentStandingsRankHeader");
        tournamentStandingsScoreHeaderLabel = root.Q<Label>("TournamentStandingsScoreHeader");
        turnLabel = root.Q<Label>("TurnLabel");
        goldLabel = root.Q<Label>("GoldLabel");
        statusLabel = root.Q<Label>("PbpStatusLabel");

        if (turnLabel == null || goldLabel == null)
        {
            if (!warnedMissingLabels)
            {
                warnedMissingLabels = true;
                Debug.LogWarning("GameplayTopHudUITKView: TurnLabel/GoldLabel not found in UIDocument source asset.", this);
            }

            uiReady = false;
            return false;
        }

        warnedMissingLabels = false;
        defaultTurnLabelWidth = turnLabel.style.width;
        defaultGoldLabelWidth = goldLabel.style.width;
        if (statusLabel != null)
        {
            defaultStatusLabelWidth = statusLabel.style.width;
        }
        SetNonInteractive(root);
        ApplySafeArea(force: true);
        responsiveSizeTierController.Apply(root);
        uiReady = true;
        return true;
    }

    private static void SetNonInteractive(VisualElement element)
    {
        if (element == null)
        {
            return;
        }

        element.pickingMode = PickingMode.Ignore;
        foreach (VisualElement child in element.Children())
        {
            SetNonInteractive(child);
        }
    }

    private void RefreshLabels()
    {
        if (!uiReady)
        {
            return;
        }

        if (ShouldShowAIVsAiBatchHud(out AIVsAIBatchRunController.ActiveRunSnapshot batchSnapshot))
        {
            ApplyAIVsAiBatchHud(batchSnapshot);
            return;
        }

        ApplyStandardHudLayout();
        turnLabel.text = BuildTurnLabel();
        goldLabel.text = BuildGoldLabel();

        if (statusLabel == null)
        {
            return;
        }

        string currentPbpGameId = turnManager.GetCurrentPlayByPostGameIdForUi();
        if (!ShouldKeepPbpSubmitStatusOverride(currentPbpGameId))
        {
            ClearPbpSubmitStatusOverride();
        }

        if (!string.IsNullOrWhiteSpace(pbpSubmitStatusOverrideMessage))
        {
            statusLabel.text = pbpSubmitStatusOverrideMessage;
            statusLabel.style.display = DisplayStyle.Flex;
            return;
        }

        PbpConnectivitySnapshot connectivity = PbpConnectivityStateModel.Resolve(Application.internetReachability);
        PbpTopHudStatusProvider.ConnectivityState connectivityState = connectivity.State switch
        {
            PbpConnectivityState.Offline => PbpTopHudStatusProvider.ConnectivityState.Unreachable,
            PbpConnectivityState.ServerUnreachable => PbpTopHudStatusProvider.ConnectivityState.ServerUnreachable,
            _ => PbpTopHudStatusProvider.ConnectivityState.Unknown
        };

        string opponentDisplayName = turnManager.GetCurrentPlayByPostOpponentTypedDisplayName();
        if (string.IsNullOrWhiteSpace(opponentDisplayName))
        {
            opponentDisplayName = "Opponent";
        }

        PbpTopHudStatusProvider.StatusResult pbpStatus =
            PbpTopHudStatusProvider.Build(turnManager, connectivityState, opponentDisplayName);

        statusLabel.text = pbpStatus.Visible ? pbpStatus.Message : string.Empty;
        statusLabel.style.display = pbpStatus.Visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private bool ShouldShowAIVsAiBatchHud(out AIVsAIBatchRunController.ActiveRunSnapshot snapshot)
    {
        snapshot = default;
        if (turnManager == null)
        {
            return false;
        }

        if (!AIVsAIBatchRunController.TryGetHudSnapshot(out snapshot))
        {
            return false;
        }

        // Treat watched batch simulation as the authoritative HUD mode for both
        // startup and active-run phases so Turn/Gold never flashes in between.
        return true;
    }

    private void ApplyAIVsAiBatchHud(AIVsAIBatchRunController.ActiveRunSnapshot snapshot)
    {
        turnLabel.style.width = 520f;
        goldLabel.style.width = 520f;

        if (snapshot.simulationMode == AIVsAIBatchRunController.SimulationMode.Tournament)
        {
            string progressLabel = snapshot.scheduledGames > 0
                ? $"Games {snapshot.completedGames}/{snapshot.scheduledGames}"
                : $"Games {snapshot.completedGames}";
            string pairingLabel = snapshot.scheduledPairings > 0
                ? $"{snapshot.scheduledPairings} pairings"
                : "Pairings pending";
            turnLabel.text =
                $"{progressLabel} | Variants {snapshot.participantCount} | {pairingLabel}";
            goldLabel.text =
                $"{FormatDurationCompact(snapshot.elapsedSeconds)} elapsed | {FormatDurationCompact(snapshot.remainingTimeSeconds)} est left";

            if (statusLabel != null)
            {
                statusLabel.style.width = 560f;
                statusLabel.text =
                    snapshot.simulationMode == AIVsAIBatchRunController.SimulationMode.Tournament
                        ? $"{snapshot.gamesPerSecond:0.00} games/s | {snapshot.tournamentStandingsPreview}"
                        : $"{snapshot.gamesPerSecond:0.00} games/s | Seat A W{snapshot.sideAWins} L{snapshot.sideBWins} D{snapshot.trueDraws} A{snapshot.aborts}";
                statusLabel.style.display = DisplayStyle.Flex;
            }

            RefreshTournamentStandingsPanel();
            return;
        }

        int completedMatches = snapshot.completedGames;
        int completedPairs = AIVsAIBatchRunController.GetCompletedPairs(completedMatches);
        string pairProgressLabel = $"Pairs {completedPairs} ({completedMatches} matches)";
        float sideAProbability = Mathf.Clamp01(snapshot.bayesianSideABetterProbability);
        bool favorSideA = sideAProbability >= 0.5f;
        string favoredSideLabel = favorSideA ? "A" : "B";
        float favoredCertainty = favorSideA ? sideAProbability : 1f - sideAProbability;
        float favoredEdgePoints = Mathf.Abs(snapshot.decisiveEdgePoints);
        float pairsPerSecond = snapshot.gamesPerSecond * 0.5f;
        string edgeLabel = favoredEdgePoints <= 0.0001f
            ? "Edge 0.0 pts"
            : $"Edge {favoredSideLabel} +{favoredEdgePoints:0.0} pts";
        string throughputLabel = $"{pairsPerSecond:0.00} pairs/s";
        int batchPairs = snapshot.batchSize / 2;
        string batchLabel = $"batch {batchPairs} pairs";

        turnLabel.text =
            $"{pairProgressLabel} | W{snapshot.sideAWins} L{snapshot.sideBWins} D{snapshot.trueDraws} A{snapshot.aborts}";
        goldLabel.text =
            $"{FormatDurationCompact(snapshot.elapsedSeconds)} elapsed | {FormatDurationCompact(snapshot.remainingTimeSeconds)} left";
        SetTournamentStandingsPanelVisible(false);

        if (statusLabel == null)
        {
            return;
        }

        statusLabel.style.width = 560f;
        statusLabel.text =
            snapshot.isWaitingForBatchAfterThreshold
                ? $"Bayes: {favoredSideLabel} {favoredCertainty:P1} / {snapshot.certaintyThreshold:P0} | {edgeLabel} | {batchLabel} | finishing batch"
                : $"Bayes: {favoredSideLabel} {favoredCertainty:P1} / {snapshot.certaintyThreshold:P0} | {edgeLabel} | {throughputLabel} | {batchLabel}";
        statusLabel.style.display = DisplayStyle.Flex;
    }

    private void ApplyStandardHudLayout()
    {
        turnLabel.style.width = defaultTurnLabelWidth;
        goldLabel.style.width = defaultGoldLabelWidth;

        if (statusLabel != null)
        {
            statusLabel.style.width = defaultStatusLabelWidth;
        }

        SetTournamentStandingsPanelVisible(false);
    }

    private string BuildTurnLabel()
    {
        if (turnManager == null)
        {
            return string.Empty;
        }

        return $"Turn {turnManager.turnNumber} - {turnManager.GetCurrentSideName()}";
    }

    private string BuildGoldLabel()
    {
        if (turnManager == null)
        {
            return string.Empty;
        }

        int displayGold = turnManager.playerGold;
        if (turnManager.currentMode == TurnManager.GameMode.PlayByPost &&
            !turnManager.isPlayerTurn)
        {
            displayGold = turnManager.aiGold;
        }

        return $"Gold {displayGold}";
    }

    private static string FormatDurationCompact(float totalSeconds)
    {
        int roundedSeconds = Mathf.Max(0, Mathf.RoundToInt(totalSeconds));
        int minutes = roundedSeconds / 60;
        int seconds = roundedSeconds % 60;
        if (minutes <= 0)
        {
            return $"{seconds}s";
        }

        return $"{minutes}m {seconds:00}s";
    }

    private void RefreshTournamentStandingsPanel()
    {
        if (tournamentStandingsPanel == null || tournamentStandingsRows == null)
        {
            return;
        }

        List<AIVsAIBatchRunController.TournamentStandingSnapshot> standings = null;
        bool shouldShow =
            Screen.width >= TournamentStandingsDesktopWidthThreshold &&
            AIVsAIBatchRunController.TryGetTournamentStandingsSnapshot(out standings) &&
            standings != null &&
            standings.Count > 0;

        SetTournamentStandingsPanelVisible(shouldShow);
        if (!shouldShow)
        {
            tournamentStandingsRows.Clear();
            return;
        }

        bool isRanked = false;
        for (int i = 0; i < standings.Count; i++)
        {
            if (standings[i].isRanked)
            {
                isRanked = true;
                break;
            }
        }

        if (tournamentStandingsTitleLabel != null)
        {
            tournamentStandingsTitleLabel.text = isRanked ? "Standings" : "Participants";
        }

        if (tournamentStandingsRankHeaderLabel != null)
        {
            tournamentStandingsRankHeaderLabel.text = isRanked ? "#" : string.Empty;
        }

        if (tournamentStandingsScoreHeaderLabel != null)
        {
            tournamentStandingsScoreHeaderLabel.text = isRanked ? "Score %" : string.Empty;
        }

        tournamentStandingsRows.Clear();
        for (int i = 0; i < standings.Count; i++)
        {
            AIVsAIBatchRunController.TournamentStandingSnapshot standing = standings[i];
            tournamentStandingsRows.Add(CreateStandingsRow(standing));
        }
    }

    private void SetTournamentStandingsPanelVisible(bool visible)
    {
        if (tournamentStandingsPanel == null)
        {
            return;
        }

        tournamentStandingsPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static VisualElement CreateStandingsRow(AIVsAIBatchRunController.TournamentStandingSnapshot standing)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("gameplay-tournament-standings-row");
        row.Add(CreateStandingsCell(
            standing.isRanked ? standing.rank.ToString() : string.Empty,
            "gameplay-tournament-standings-cell-rank"));
        row.Add(CreateStandingsCell(
            standing.label,
            "gameplay-tournament-standings-cell-variant"));
        row.Add(CreateStandingsCell(
            standing.games.ToString(),
            "gameplay-tournament-standings-cell-games"));
        row.Add(CreateStandingsCell(
            standing.wins.ToString(),
            "gameplay-tournament-standings-cell-small"));
        row.Add(CreateStandingsCell(
            standing.losses.ToString(),
            "gameplay-tournament-standings-cell-small"));
        row.Add(CreateStandingsCell(
            standing.draws.ToString(),
            "gameplay-tournament-standings-cell-small"));
        row.Add(CreateStandingsCell(
            standing.isRanked ? standing.scoreRate.ToString("P0") : "--",
            "gameplay-tournament-standings-cell-score"));
        return row;
    }

    private static Label CreateStandingsCell(string text, string modifierClass)
    {
        Label label = new Label(text);
        label.AddToClassList("gameplay-tournament-standings-cell");
        label.AddToClassList(modifierClass);
        label.tooltip = text;
        return label;
    }

    private void ApplySafeArea(bool force)
    {
        if (!uiReady || root == null)
        {
            return;
        }

        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        if (screenSize.x <= 0 || screenSize.y <= 0)
        {
            return;
        }

        if (!force && safeArea == lastSafeArea && screenSize == lastScreenSize)
        {
            return;
        }

        lastSafeArea = safeArea;
        lastScreenSize = screenSize;

        float leftInset = safeArea.xMin;
        float rightInset = screenSize.x - safeArea.xMax;
        float topInset = screenSize.y - safeArea.yMax;

        VisualElement safeAreaTarget = hudRoot ?? root;
        safeAreaTarget.style.paddingLeft = leftInset;
        safeAreaTarget.style.paddingRight = rightInset;
        safeAreaTarget.style.paddingTop = topInset;
        safeAreaTarget.style.paddingBottom = 0f;
    }

    private void TrySubscribeToTurnManagerEvents()
    {
        if (turnManager == null)
        {
            return;
        }

        if (subscribedTurnManager == turnManager)
        {
            return;
        }

        UnsubscribeFromTurnManagerEvents();
        turnManager.PlayByPostSubmitResult += HandlePlayByPostSubmitResult;
        turnManager.PlayByPostFetchResult += HandlePlayByPostFetchResult;
        subscribedTurnManager = turnManager;
    }

    private void UnsubscribeFromTurnManagerEvents()
    {
        if (subscribedTurnManager == null)
        {
            return;
        }

        subscribedTurnManager.PlayByPostSubmitResult -= HandlePlayByPostSubmitResult;
        subscribedTurnManager.PlayByPostFetchResult -= HandlePlayByPostFetchResult;
        subscribedTurnManager = null;
    }

    private void HandlePlayByPostSubmitResult(bool ok, string err)
    {
        if (turnManager == null || turnManager.currentMode != TurnManager.GameMode.PlayByPost)
        {
            return;
        }

        PbpConnectivityStateModel.ObserveSubmitResult(ok, err);
        if (ok)
        {
            ClearPbpSubmitStatusOverride();
            return;
        }

        if (!turnManager.IsCurrentPlayByPostFirstRemoteSubmitPendingForUi())
        {
            return;
        }

        string gameId = turnManager.GetCurrentPlayByPostGameIdForUi();
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        pbpSubmitStatusOverrideGameId = gameId;
        pbpSubmitStatusOverrideMessage = BuildFirstTurnSubmitFailureMessage(err);
    }

    private void HandlePlayByPostFetchResult(bool reachable, string resultOrError)
    {
        if (turnManager == null || turnManager.currentMode != TurnManager.GameMode.PlayByPost)
        {
            return;
        }

        PbpConnectivityStateModel.ObserveFetchResult(reachable, resultOrError);
        if (reachable && string.Equals(resultOrError, "OK", StringComparison.OrdinalIgnoreCase))
        {
            ClearPbpSubmitStatusOverride();
        }
    }

    private bool ShouldKeepPbpSubmitStatusOverride(string currentPbpGameId)
    {
        return !string.IsNullOrWhiteSpace(pbpSubmitStatusOverrideMessage) &&
               !string.IsNullOrWhiteSpace(pbpSubmitStatusOverrideGameId) &&
               string.Equals(currentPbpGameId, pbpSubmitStatusOverrideGameId, StringComparison.Ordinal) &&
               turnManager != null &&
               turnManager.IsCurrentPlayByPostFirstRemoteSubmitPendingForUi();
    }

    private void ClearPbpSubmitStatusOverride()
    {
        pbpSubmitStatusOverrideMessage = null;
        pbpSubmitStatusOverrideGameId = null;
    }

    private static string BuildFirstTurnSubmitFailureMessage(string err)
    {
        if (string.Equals(err, "UNAUTHORIZED", StringComparison.Ordinal))
        {
            return FirstTurnSubmitUnauthorizedMessage;
        }

        return FirstTurnSubmitFailedMessage;
    }

    private void DisableOverlay()
    {
        if (uiDocument != null)
        {
            uiDocument.enabled = false;
        }

        ClearUiCache();
    }

    private void ClearUiCache()
    {
        responsiveSizeTierController.Reset(root);
        root = null;
        hudRoot = null;
        tournamentStandingsPanel = null;
        tournamentStandingsRows = null;
        tournamentStandingsTitleLabel = null;
        tournamentStandingsRankHeaderLabel = null;
        tournamentStandingsScoreHeaderLabel = null;
        turnLabel = null;
        goldLabel = null;
        statusLabel = null;
        defaultTurnLabelWidth = default;
        defaultGoldLabelWidth = default;
        defaultStatusLabelWidth = default;
        pbpSubmitStatusOverrideMessage = null;
        pbpSubmitStatusOverrideGameId = null;
        uiReady = false;
        lastSafeArea = Rect.zero;
        lastScreenSize = Vector2Int.zero;
    }
}
