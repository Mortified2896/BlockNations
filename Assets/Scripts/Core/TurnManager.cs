// CHANGELOG (PBp Auto Sync):
// - Added transport abstraction + provider wiring for automatic Play-by-Post turn sync.
// - Extracted `ApplyLoadedSave` and added `LoadFromJsonString` for transport/clipboard loads.
// - Added PBp auto-submit + polling loop and `PlayByPostSyncNow` (manual fetch button hook).
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class TurnManager : MonoBehaviour
{
    [System.Serializable]
    public sealed class OfficialUnitRegistration
    {
        public string unitTypeId;
        public GameObject prefab;
        public bool recruitable = true;
        public int recruitDisplayOrder;
    }

    public enum GameMode
    {
        None,
        VsAI,
        PlayByPost
    }

    public enum AIRecruitVariant
    {
        Default,
        RiderFocus
    }

    public enum AIDebugProfile
    {
        Baseline
    }

    public enum AIVsAIBatchSpeedPreset
    {
        Normal,
        Fast,
        VeryFast,
        UltraFast
    }

    public enum MapSizePreset
    {
        Unspecified,
        Small,
        Large
    }

    //Dev only - Local seat selection for Play-by-Post testing only

    public enum LocalSeat
    {
        Player1,
        Player2
    }

    public static TurnManager Instance { get; private set; }

    [Header("Dev only - Play-by-Post")]
    public LocalSeat localSeat = LocalSeat.Player1;

    [Header("Mode")]
    public GameMode currentMode = GameMode.None;

    [Header("Turn State")]
    public bool isPlayerTurn = true;
    public int currentTurnSeatIndex = 0;
    public int turnNumber = 1;
    public bool gameOver = false;

    [Header("Economy")]
    // Base starting gold; income from cities adds on top at game start.
    public int startingGold = 2;
    public int playerGold = 2;
    public int aiGold = 0;
    public int goldPerCity = 1;
    public int warriorCost => GetRecruitCost(UnitRegistry.WarriorTypeId);
    private readonly List<int> seatGold = new List<int>();

    [Header("AI Settings")]
    public float aiTurnDelay = 1f; // seconds the AI "thinks" before ending its turn
    [Tooltip("Experimental/dev-only AI recruit override. Default preserves current behavior.")]
    public AIRecruitVariant aiRecruitVariant = AIRecruitVariant.Default;
    [Tooltip("Experimental/dev-only local AI features. Default preserves current behavior.")]
    public AILocalDecisionFeatures aiLocalDecisionFeatures = AILocalDecisionFeatures.None;

    [Header("UI")]
    [Tooltip("Optional: assign End Turn / Next Turn button to keep interactable state synced with CanAdvanceTurn().")]
    public Button endTurnButton;

    [Header("Play By Post - Transport")]
    public bool playByPostAutoSyncEnabled = true;
    public float playByPostPollSeconds = 3f;
    [Tooltip("Optional: a component that implements ITurnTransport. Overrides provider/default if set.")]
    public MonoBehaviour turnTransportComponent;
    [Tooltip("Optional: used to construct an ITurnTransport implementation when none is provided.")]
    public TurnTransportProvider transportProvider;

    [Header("Telemetry")]
    [Tooltip("Optional: a component that implements ITurnTelemetrySink.")]
    public MonoBehaviour telemetrySinkComponent;

    [Header("References")]
    public GridManager gridManager;
    public int visibilityRadius = 1;

    [Header("Prefabs")]
    public GameObject unitPrefab; // used to respawn units on load

    [Header("Official Units")]
    public List<OfficialUnitRegistration> officialUnitRegistrations = new List<OfficialUnitRegistration>();

    [Header("Audio")]
    public bool playMusicOnStart = true;
    public AudioClip gameplayMusic;

    [Header("Saving")]
    public bool autoSaveEnabled = true;
    public string autoSaveFileName = "save.json";
    public bool playByPostExportPretty = true;

    [Header("AI Debug")]
    public bool disableAI = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Tooltip("Debug only: when enabled in VsAI, both sides are driven by the existing AI turn flow.")]
    public bool enableAIVsAIDebugMode = false;
#endif

    [Header("Quality of Life")]
    [Tooltip("Vs AI only: if the player has no legal moves or recruit actions, automatically end the turn.")]
    public bool autoEndTurnWhenNoActions = true;
    [Tooltip("Wait this long (real time) after an action/turn start before auto-ending.")]
    public float autoEndTurnDelaySeconds = 0.6f;
    [Tooltip("Don't auto-end within this many seconds of the last player input (real time).")]
    public float autoEndTurnInputCooldownSeconds = 0.8f;

    private bool isLoadingFromSave = false;
    // When true in PlayByPost, the current side has ended
    // their turn and we are only waiting for the player
    // to copy/export the JSON state. While this is true,
    // no further actions should be taken in the scene.
    private bool isPlayByPostWaitingForExport = false;
    private ITurnTransport turnTransport;
    private ITurnTelemetrySink telemetrySink = NullTurnTelemetrySink.Instance;
    private Coroutine playByPostPollRoutine;
    private Coroutine pbpIncomingAudioRoutine;
    private int lastAppliedTurnNumberForPolling = 0;
    private bool isPlayByPostFetchInProgress = false;
    private bool isApplyingFetchedPlayByPostSnapshot = false;
    private bool playByPostLastFetchWasNoTurn = false;
    private float playByPostLastNoTurnLogTime = -999f;
    private Coroutine aiVsAiDebugRoutine;
    private AIRecruitVariant aiVsAiSideARecruitVariant = AIRecruitVariant.Default;
    private AIRecruitVariant aiVsAiSideBRecruitVariant = AIRecruitVariant.Default;
    private bool aiVsAiDebugPaused = false;
    private const string BottomRightControlNextLabel = "Next";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private AILocalDecisionFeatures aiVsAiSideAFeatures = AILocalDecisionFeatures.None;
    private AILocalDecisionFeatures aiVsAiSideBFeatures = AILocalDecisionFeatures.None;
    private AIDebugProfile aiVsAiSideAProfile = AIDebugProfile.Baseline;
    private AIDebugProfile aiVsAiSideBProfile = AIDebugProfile.Baseline;
    private AIVsAIBatchSpeedPreset aiVsAiBatchSpeedPreset = AIVsAIBatchSpeedPreset.Normal;
    private bool aiVsAiDebugRestartPending = false;
    private bool aiVsAiCompletedTournamentAutoRestartPending = false;
    private string aiVsAiCompletedTournamentAutoRestartMessage = string.Empty;
    private const string AIVsAISimulationCompleteTitle = "AI Simulation Complete";
    private const string AIVsAISimulationAbortedTitle = "AI Simulation Aborted";
    private const string AIVsAITournamentPausedTitle = "Tournament Paused";
    private const string BottomRightControlBatchPauseLabel = "Pause";
    private const string BottomRightControlBatchResumeLabel = "Resume";
    private const string BottomRightControlBatchStopLabel = "Stop";
    private const string BottomRightControlBatchContinueTournamentLabel = "Continue Tournament";
    private const string AIVsAIDebugAbortWinner = "Abort";
    private const int AIVsAIBatchTurnLimit = 200;
    private const float AIVsAIFastTurnDelaySeconds = 0.2f;
    private const float AIVsAIFastRestartDelaySeconds = 0.15f;
    private const float AIVsAIVeryFastTurnDelaySeconds = 0.05f;
    private const float AIVsAIVeryFastRestartDelaySeconds = 0.03f;
    private const float AIVsAIUltraFastTurnDelaySeconds = 0f;
    private const float AIVsAIUltraFastRestartDelaySeconds = 0f;
#endif
#if DEVELOPMENT_BUILD
    private int lastSubmittedTransportSeqForTelemetry = -1;
    private string lastSubmittedGameIdForTelemetry;
#endif
#if DEVELOPMENT_BUILD || UNITY_EDITOR
    private bool hasLoggedManifestProbeFailure = false;
    private string lastPbpControlReadinessBlockKey;
    private string lastPbpSeatAdoptionLogKey;
    private float lastPbpInputDeniedLogTime = -999f;
    private const float PbpInputDeniedLogCooldownSeconds = 1f;
    private float lastPbpSelectionGateLogTime = -999f;
    private const float PbpSelectionGateLogCooldownSeconds = 1f;
#endif
    private bool pbpControlReadinessReady = false;
    private string pbpCreatorBootstrapGameId;
    private bool pbpCreatorFirstRemoteSubmitPending;
    private const float PlayByPostNoTurnLogCooldownSeconds = 5f;
    private const string PlayByPostGameIdKeyRaw = "pbp_gameId";
    private const string PlayByPostForceNewKeyRaw = "pbp_forceNew";
    private const string PlayByPostPendingNewGameIdKeyRaw = "pbp_pendingNewGameId";
    private const string PlayByPostPrimarySaveFileName = "save.json";
    private const string SinglePlayerPrimarySaveFileName = "save_sp.json";
    private const string PlayByPostPerGameSaveFolderName = "pbp";
    private const string PlayByPostPerGameSavePrefix = "pbp_";
    private const string ReturnToMultiplayerPaneKeyRaw = "ui_returnToMultiplayerPane";
    private static string PlayByPostGameIdKey => DevClientInstanceScope.ScopePlayerPrefsKey(PlayByPostGameIdKeyRaw);
    private static string PlayByPostForceNewKey => DevClientInstanceScope.ScopePlayerPrefsKey(PlayByPostForceNewKeyRaw);
    private static string PlayByPostPendingNewGameIdKey => DevClientInstanceScope.ScopePlayerPrefsKey(PlayByPostPendingNewGameIdKeyRaw);
    private static string ReturnToMultiplayerPaneKey => DevClientInstanceScope.ScopePlayerPrefsKey(ReturnToMultiplayerPaneKeyRaw);
    private const string MainMenuSceneName = "MainMenu";
    private const string DefaultGameOverMessage = "Game Over";
    private const string DefaultGameOverTitle = "Game Over";
    private const string DefaultGameOverPrimaryButtonLabel = "Play Again";
    private const string PbpVersionMismatchTitle = "Update Required";
    private const string PbpVersionMismatchInGameMessage =
        "This PbP game was created or updated with a newer version of BlockNations and cannot continue on this build. Please update the app to continue.";
    private const string PbpAppVersionMismatchInGameMessage =
        "This PbP game was created or updated with a different version of BlockNations and cannot continue on this build. Both players must use the same app version.";
    private const string PbpVersionMismatchExitButtonLabel = "Back to Multiplayer";
    // PbP compatibility policy:
    // - Bump appVersion when the build identity changes and mixed-build PbP play should be blocked.
    // - Bump protocolVersion only when serialized PbP payload meaning changes and older payloads
    //   cannot be read safely as-is.
    // - Keep at most one explicit migration source at a time: the immediately previous protocol.
    private const int SupportedPbpMigrationProtocolVersion = 4;
    private const int SupportedPbpProtocolVersion = 5;
    public const int SmallBoardWidth = 11;
    public const int SmallBoardHeight = 11;
    private const int LegacySmallBoardWidth = 10;
    private const int LegacySmallBoardHeight = 10;
    public const int LargeBoardWidth = 15;
    public const int LargeBoardHeight = 15;
    public static int PbpProtocolVersion => SupportedPbpProtocolVersion;
    public static string CurrentAppVersion => string.IsNullOrWhiteSpace(Application.version)
        ? "unknown"
        : Application.version.Trim();

    public static bool IsSupportedPbpLoadProtocolVersion(int protocolVersion)
    {
        return protocolVersion == SupportedPbpProtocolVersion ||
               protocolVersion == SupportedPbpMigrationProtocolVersion;
    }

    public static bool IsSupportedPbpAppVersion(string appVersion)
    {
        return !string.IsNullOrWhiteSpace(appVersion) &&
               string.Equals(appVersion.Trim(), CurrentAppVersion, System.StringComparison.Ordinal);
    }

    public static MapSizePreset GetDefaultMapSizePreset()
    {
        return MapSizePreset.Small;
    }

    public string GetCurrentPlayByPostGameIdForUi()
    {
        return GetPbpGameIdFromPrefsOrCurrent();
    }

    public bool IsCurrentPlayByPostCreatorGameForUi(string gameId)
    {
        if (currentMode != GameMode.PlayByPost || string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(pbpCreatorBootstrapGameId) &&
               string.Equals(gameId, pbpCreatorBootstrapGameId, System.StringComparison.Ordinal);
    }

    public bool ShouldShowPlayByPostSharePromptForUi(string gameId)
    {
        if (currentMode != GameMode.PlayByPost ||
            gameOver ||
            string.IsNullOrWhiteSpace(gameId) ||
            !string.Equals(gameId, currentGameId, System.StringComparison.Ordinal) ||
            !isPlayByPostWaitingForExport)
        {
            return false;
        }

        return IsCurrentPlayByPostCreatorGameForUi(gameId) || GetLocalIsPlayerOneForGame(gameId, out _, out _);
    }

    public bool IsCurrentPlayByPostFirstRemoteSubmitPendingForUi()
    {
        return currentMode == GameMode.PlayByPost &&
               !string.IsNullOrWhiteSpace(currentGameId) &&
               !string.IsNullOrWhiteSpace(pbpCreatorBootstrapGameId) &&
               string.Equals(currentGameId, pbpCreatorBootstrapGameId, System.StringComparison.Ordinal) &&
               pbpCreatorFirstRemoteSubmitPending;
    }

    public bool ShouldShowPlayByPostResignInSettingsForUi()
    {
        return currentMode == GameMode.PlayByPost &&
               !gameOver &&
               !string.IsNullOrWhiteSpace(currentGameId);
    }

    public bool CanUsePlayByPostResignInSettingsForUi()
    {
        if (!ShouldShowPlayByPostResignInSettingsForUi() ||
            playByPostResignationSubmitInFlight ||
            isPlayByPostFetchInProgress)
        {
            return false;
        }

        if (!TryGetLocalSeatIndexForPbp(currentGameId, out _))
        {
            return false;
        }

        return CanLocalPlayerIssueCommands();
    }

    public void RequestPlayByPostResignationFromSettingsForUi()
    {
        if (!CanUsePlayByPostResignInSettingsForUi())
        {
            return;
        }

        LocalPlayerProfileStore.ProfileData profile = LocalPlayerProfileStore.GetOrCreateProfile();
        GameSave current = BuildCurrentSave();
        if (current == null)
        {
            return;
        }

        string currentJson = JsonUtility.ToJson(current, false);
        if (string.IsNullOrWhiteSpace(currentJson) ||
            !TryGetLocalSeatIndexForPbp(currentGameId, out int localSeat) ||
            !TryBuildPlayByPostResignationJson(
                currentJson,
                currentGameId,
                localSeat,
                profile.PlayerId,
                profile.TypedDisplayName,
                out string resignedJson,
                out int exportTurnNumber,
                out bool exportIsPlayerTurn,
                out _,
                out int exportTransportSeq,
                out int exportSeatCount,
                out bool exportGameOver))
        {
            return;
        }

        ResolveTurnTransport();
        if (turnTransport == null || !turnTransport.IsAvailable)
        {
            return;
        }

        playByPostResignationSubmitInFlight = true;
        StartCoroutine(SubmitPlayByPostResignationAndArchive(
            resignedJson,
            exportTurnNumber,
            exportIsPlayerTurn,
            exportTransportSeq,
            exportSeatCount,
            exportGameOver));
    }

    public static void GetBoardDimensionsForPreset(MapSizePreset preset, out int boardWidth, out int boardHeight)
    {
        switch (preset)
        {
            case MapSizePreset.Small:
                boardWidth = SmallBoardWidth;
                boardHeight = SmallBoardHeight;
                return;

            case MapSizePreset.Large:
            case MapSizePreset.Unspecified:
            default:
                boardWidth = LargeBoardWidth;
                boardHeight = LargeBoardHeight;
                return;
        }
    }

    public static MapSizePreset ResolveMapSizePreset(int boardWidth, int boardHeight)
    {
        if ((boardWidth == SmallBoardWidth && boardHeight == SmallBoardHeight) ||
            (boardWidth == LegacySmallBoardWidth && boardHeight == LegacySmallBoardHeight))
        {
            return MapSizePreset.Small;
        }

        if (boardWidth == LargeBoardWidth && boardHeight == LargeBoardHeight)
        {
            return MapSizePreset.Large;
        }

        return GetDefaultMapSizePreset();
    }

    public static MapSizePreset ParseMapSizePresetOrDefault(string rawPreset)
    {
        if (!string.IsNullOrWhiteSpace(rawPreset) &&
            System.Enum.TryParse(rawPreset, out MapSizePreset parsedPreset) &&
            parsedPreset != MapSizePreset.Unspecified)
        {
            return parsedPreset;
        }

        return GetDefaultMapSizePreset();
    }

    // Controlled via Unity Scripting Define Symbols:
    // ENABLE_AUTO_END_TURN_ON_NO_ACTIONS
    // TODO: To re-enable, define the symbol above.
#if ENABLE_AUTO_END_TURN_ON_NO_ACTIONS
    private const bool autoEndTurnOnNoActionsEnabled = true;
#else
    private const bool autoEndTurnOnNoActionsEnabled = false;
#endif
    private bool autoEndTurnDisabledLoggedThisTurn = false;
    private Coroutine autoEndTurnRoutine;
    private float lastHumanInputUnscaledTime = -999f;
    private bool hasCachedEndTurnButtonInteractable;
    private bool cachedEndTurnButtonInteractable;
    private enum PbpEndgamePrimaryAction
    {
        None,
        Submitting,
        RetrySubmit,
        BackToMultiplayer,
        BackAndArchive
    }

    private enum GameOverSecondaryAction
    {
        None,
        MainMenu,
        BackToAIVsAISettings
    }

    private enum GameOverTertiaryAction
    {
        None,
        RestartAIVsAIBatch,
        ResumeAIVsAIBatch
    }

    private enum GameOverPrimaryUiAction
    {
        ReplayCurrentMode,
        CopyGameOverMessage
    }

    public enum BottomRightControlKind
    {
        DefaultNext,
        BatchPause,
        BatchResume,
        BatchStopPendingRestart,
        BatchContinueTournament
    }

    public readonly struct BottomRightControlUiState
    {
        public readonly BottomRightControlKind kind;
        public readonly string label;
        public readonly bool interactable;

        public BottomRightControlUiState(
            BottomRightControlKind kind,
            string label,
            bool interactable)
        {
            this.kind = kind;
            this.label = string.IsNullOrWhiteSpace(label) ? BottomRightControlNextLabel : label;
            this.interactable = interactable;
        }
    }

    private PbpEndgamePrimaryAction pbpEndgamePrimaryAction = PbpEndgamePrimaryAction.None;
    private bool pbpEndgameLocalWinner = false;
    private bool pbpEndgameSubmitPending = false;
    private bool pbpEndgameSubmitSucceeded = false;
    private bool pbpEndgameSubmitPayloadCached = false;
    private string pbpEndgameCachedExportJson;
    private int pbpEndgameCachedTransportSeq;
    private int pbpEndgameCachedExportTurnNumber;
    private bool pbpEndgameCachedExportIsPlayerTurn;
    private string pbpEndgameCachedExportGameId;
    private int pbpWinnerSeatIndex = -1;
    private string gameOverUiTitle = string.Empty;
    private string gameOverUiMessage = string.Empty;
    private string gameOverUiPrimaryButtonLabel = DefaultGameOverPrimaryButtonLabel;
    private bool gameOverUiPrimaryButtonInteractable = true;
    private GameOverPrimaryUiAction gameOverUiPrimaryAction = GameOverPrimaryUiAction.ReplayCurrentMode;
    private string gameOverUiSecondaryButtonLabel = string.Empty;
    private bool gameOverUiSecondaryButtonVisible = false;
    private bool gameOverUiSecondaryButtonInteractable = false;
    private GameOverSecondaryAction gameOverUiSecondaryAction = GameOverSecondaryAction.None;
    private string gameOverUiTertiaryButtonLabel = string.Empty;
    private bool gameOverUiTertiaryButtonVisible = false;
    private bool gameOverUiTertiaryButtonInteractable = false;
    private GameOverTertiaryAction gameOverUiTertiaryAction = GameOverTertiaryAction.None;
    private bool aiVsAiPausedTournamentSummaryVisible = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private string lastBottomRightControlTraceSignature = string.Empty;
    private AIVsAIBatchRunController.SimulationSettings gameOverUiRepeatAIVsAISimulationSettings =
        AIVsAIBatchRunController.GetDefaultSimulationSettings();
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private AIRecruitVariant gameOverUiRepeatSideARecruitVariant = AIRecruitVariant.Default;
    private AIRecruitVariant gameOverUiRepeatSideBRecruitVariant = AIRecruitVariant.Default;
    private AILocalDecisionFeatures gameOverUiRepeatSideAFeatures = AILocalDecisionFeatures.None;
    private AILocalDecisionFeatures gameOverUiRepeatSideBFeatures = AILocalDecisionFeatures.None;
    private AIDebugProfile gameOverUiRepeatSideAProfile = AIDebugProfile.Baseline;
    private AIDebugProfile gameOverUiRepeatSideBProfile = AIDebugProfile.Baseline;
#endif
    [System.Serializable]
    private class SavedCity
    {
        public int x;
        public int y;
        public int ownerSeatIndex = -1;
        public bool isPlayerOwned;
        public bool hasRecruitedThisTurn;
    }

    [System.Serializable]
    private class SavedUnit
    {
        public string unitTypeId;
        public int ownerSeatIndex = -1;
        public bool isPlayerOwned;
        public float x;
        public float y;
        public float z;
        // Protocol 4+ uses scale-10 combat units for persisted health.
        public int currentHealthUnits;
        // Legacy protocol/local saves used whole-number health.
        public int currentHealth;
        public int movesUsedThisTurn;
        public int attacksUsedThisTurn;
        public bool hasAttackedThisTurn;
    }

    [System.Serializable]
    private class SavedTile
    {
        public int x;
        public int y;
        public List<int> seenSeatIndices = new List<int>();
        public bool playerSeen;
        public bool opponentSeen;
    }

    private struct PbpIncomingAudioUnitSummary
    {
        public int playerUnitCount;
        public int opponentUnitCount;
    }

    [System.Serializable]
    private class GameSave
    {
        public string version = "3";
        public int protocolVersion;
        public string appVersion;
        public string gameId;
        public string mode;
        public string aiRecruitVariant;
        public string mapSizePreset;
        public int boardWidth;
        public int boardHeight;
        public bool isPlayerTurn;
        public List<int> seatGold = new List<int>();
        public int turnNumber;
        public int playerGold;
        public int aiGold;
        public bool gameOver;
        public bool hasWinnerSeatIndex;
        public int winnerSeatIndex = -1;
        public int visibilityRadius;
        public int seatCount = PlayByPostSeatUtility.MinSeatCount;
        public int currentTurnSeatIndex;
        public int transportSeq;
        public string playerOneTypedDisplayName;
        public string playerTwoTypedDisplayName;
        public List<PlayByPostSeatMetadata> seats = new List<PlayByPostSeatMetadata>();
        public List<SavedCity> cities = new List<SavedCity>();
        public List<SavedUnit> units = new List<SavedUnit>();
        public List<SavedTile> tiles = new List<SavedTile>();
    }

    // Stable id for the current campaign/save chain so exports can be shared
    private string currentGameId;
    private string typedDisplayMetadataGameId;
    private string knownPlayerOneTypedDisplayName;
    private string knownPlayerTwoTypedDisplayName;
    private readonly List<PlayByPostSeatMetadata> runtimePlayByPostSeatMetadata = new List<PlayByPostSeatMetadata>();
    private int configuredPlayByPostSeatCount = PlayByPostSeatUtility.MinSeatCount;
    private bool playByPostResignationSubmitInFlight;
    private string cachedGameIdRaw;
    private string cachedGameIdHash;
    public event System.Action<bool, string> PlayByPostSubmitResult;
    public event System.Action<bool, string> PlayByPostFetchResult;
    public bool IsGameOverUiVisible =>
        (gameOver || aiVsAiPausedTournamentSummaryVisible) &&
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        !aiVsAiDebugRestartPending;
#else
        true;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool HasAIVsAIBatchRunActiveForUi()
    {
        return AIVsAIBatchRunController.TryGetActiveRunSnapshot(out _);
    }

    public bool IsAIVsAIBatchRunActiveForUi()
    {
        return HasAIVsAIBatchRunActiveForUi();
    }

    public bool IsAIVsAIBatchHudVisibleForUi()
    {
        return AIVsAIBatchRunController.TryGetHudSnapshot(out _);
    }

    public bool IsAIVsAICompletedTournamentAutoRestartPendingForUi() =>
        aiVsAiDebugRestartPending &&
        aiVsAiCompletedTournamentAutoRestartPending;

    public bool IsContinuousTournamentBatchControlActiveForUi()
    {
        if (IsAIVsAICompletedTournamentAutoRestartPendingForUi())
        {
            return true;
        }

        if (AIVsAIBatchRunController.TryGetActiveSimulationSettings(out AIVsAIBatchRunController.SimulationSettings activeSettings))
        {
            return activeSettings.mode == AIVsAIBatchRunController.SimulationMode.Tournament &&
                   activeSettings.tournamentRunContinuously;
        }

        return gameOver &&
               gameOverUiRepeatAIVsAISimulationSettings.mode == AIVsAIBatchRunController.SimulationMode.Tournament &&
               gameOverUiRepeatAIVsAISimulationSettings.tournamentRunContinuously;
    }
#else
    public bool IsAIVsAIBatchRunActiveForUi() => false;
    public bool IsAIVsAIBatchHudVisibleForUi() => false;
    public bool IsAIVsAICompletedTournamentAutoRestartPendingForUi() => false;
    public bool IsContinuousTournamentBatchControlActiveForUi() => false;
#endif
    public string GameOverUiTitle =>
        string.IsNullOrWhiteSpace(gameOverUiTitle) ? DefaultGameOverTitle : gameOverUiTitle;
    public string GameOverUiMessage =>
        string.IsNullOrWhiteSpace(gameOverUiMessage) ? DefaultGameOverMessage : gameOverUiMessage;
    public string GameOverUiPrimaryButtonLabel =>
        string.IsNullOrWhiteSpace(gameOverUiPrimaryButtonLabel) ? DefaultGameOverPrimaryButtonLabel : gameOverUiPrimaryButtonLabel;
    public bool GameOverUiPrimaryButtonInteractable => gameOverUiPrimaryButtonInteractable;
    public bool GameOverUiSecondaryButtonVisible => gameOverUiSecondaryButtonVisible;
    public string GameOverUiSecondaryButtonLabel => gameOverUiSecondaryButtonLabel;
    public bool GameOverUiSecondaryButtonInteractable => gameOverUiSecondaryButtonInteractable;
    public bool GameOverUiTertiaryButtonVisible => gameOverUiTertiaryButtonVisible;
    public string GameOverUiTertiaryButtonLabel => gameOverUiTertiaryButtonLabel;
    public bool GameOverUiTertiaryButtonInteractable => gameOverUiTertiaryButtonInteractable;
    public bool IsPbpEndgameMenuExitBlocked =>
        currentMode == GameMode.PlayByPost &&
        gameOver &&
        pbpEndgameLocalWinner &&
        pbpEndgameSubmitPending;
    public bool IsPbpEndgameResolvedCompletedForUi =>
        currentMode == GameMode.PlayByPost &&
        gameOver &&
        pbpEndgamePrimaryAction == PbpEndgamePrimaryAction.BackAndArchive &&
        (!pbpEndgameLocalWinner || pbpEndgameSubmitSucceeded);

    public bool IsHumanTurn()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsAIVsAIDebugModeActive())
            return false;
#endif
        if (currentMode == GameMode.PlayByPost && isPlayByPostWaitingForExport)
            return false;

        if (currentMode == GameMode.None)
            return false;

        if (currentMode == GameMode.VsAI)
            return isPlayerTurn;

        return currentMode == GameMode.PlayByPost;
    }

    public bool CanAdvanceTurn()
    {
        if (gameOver)
            return false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsAIVsAIDebugModeActive())
            return false;
#endif
        if (currentMode == GameMode.PlayByPost && isPlayByPostWaitingForExport)
            return false;

        if (currentMode == GameMode.None)
            return false;

        if (currentMode == GameMode.VsAI)
            return isPlayerTurn;

        // Play-by-Post: only allow advancing when it's this local seat's turn.
        if (currentMode == GameMode.PlayByPost)
            return CanLocalPlayerIssueCommands();

        return false;
    }

    public int GetRecruitCost(string unitTypeId)
    {
        return UnitRegistry.GetDefinitionOrDefault(unitTypeId).RecruitCost;
    }

    public List<UnitDefinition> GetRecruitableOfficialUnitDefinitions()
    {
        List<UnitDefinition> recruitableDefinitions = new List<UnitDefinition>();
        if (officialUnitRegistrations == null)
        {
            return recruitableDefinitions;
        }

        for (int i = 0; i < officialUnitRegistrations.Count; i++)
        {
            OfficialUnitRegistration registration = officialUnitRegistrations[i];
            if (registration == null || !registration.recruitable)
            {
                continue;
            }

            string normalizedTypeId = UnitRegistry.NormalizeTypeId(registration.unitTypeId);
            if (!UnitRegistry.TryGetDefinition(normalizedTypeId, out UnitDefinition definition))
            {
                continue;
            }

            bool alreadyIncluded = false;
            for (int existingIndex = 0; existingIndex < recruitableDefinitions.Count; existingIndex++)
            {
                if (string.Equals(recruitableDefinitions[existingIndex].TypeId, definition.TypeId, System.StringComparison.Ordinal))
                {
                    alreadyIncluded = true;
                    break;
                }
            }

            if (!alreadyIncluded)
            {
                recruitableDefinitions.Add(definition);
            }
        }

        recruitableDefinitions.Sort(CompareRecruitableDefinitions);
        return recruitableDefinitions;
    }

    public GameObject GetUnitPrefabForType(string unitTypeId)
    {
        if (!UnitRegistry.TryGetDefinition(unitTypeId, out UnitDefinition definition))
        {
            return null;
        }

        string resolvedPrefabTypeId = UnitRegistry.NormalizeTypeId(definition.PrefabTypeId);
        OfficialUnitRegistration registration = GetOfficialUnitRegistration(resolvedPrefabTypeId);
        return registration != null ? registration.prefab : null;
    }

    private OfficialUnitRegistration GetOfficialUnitRegistration(string unitTypeId)
    {
        if (officialUnitRegistrations == null)
        {
            return null;
        }

        string normalizedTypeId = UnitRegistry.NormalizeTypeId(unitTypeId);
        for (int i = 0; i < officialUnitRegistrations.Count; i++)
        {
            OfficialUnitRegistration registration = officialUnitRegistrations[i];
            if (registration == null)
            {
                continue;
            }

            if (string.Equals(UnitRegistry.NormalizeTypeId(registration.unitTypeId), normalizedTypeId, System.StringComparison.Ordinal))
            {
                return registration;
            }
        }

        return null;
    }

    private int CompareRecruitableDefinitions(UnitDefinition left, UnitDefinition right)
    {
        if (left == right)
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        OfficialUnitRegistration leftRegistration = GetOfficialUnitRegistration(left.TypeId);
        OfficialUnitRegistration rightRegistration = GetOfficialUnitRegistration(right.TypeId);
        int leftOrder = leftRegistration != null ? leftRegistration.recruitDisplayOrder : int.MaxValue;
        int rightOrder = rightRegistration != null ? rightRegistration.recruitDisplayOrder : int.MaxValue;
        int orderComparison = leftOrder.CompareTo(rightOrder);
        if (orderComparison != 0)
        {
            return orderComparison;
        }

        int displayNameComparison = string.Compare(left.DisplayName, right.DisplayName, System.StringComparison.Ordinal);
        if (displayNameComparison != 0)
        {
            return displayNameComparison;
        }

        return string.Compare(left.TypeId, right.TypeId, System.StringComparison.Ordinal);
    }

    public GameObject InstantiateConfiguredUnit(
        string unitTypeId,
        GameObject prefab,
        Vector3 position,
        int ownerSeatIndex,
        City currentCity,
        bool resetTurnState)
    {
        if (prefab == null)
        {
            return null;
        }

        if (!UnitRegistry.TryGetDefinition(unitTypeId, out UnitDefinition definition))
        {
            return null;
        }

        GameObject spawnedObject = Instantiate(prefab, position, Quaternion.identity);
        Unit unit = spawnedObject.GetComponent<Unit>();
        if (unit != null)
        {
            if (!unit.ApplyDefinition(definition.TypeId, preserveCurrentHealth: false))
            {
                Destroy(spawnedObject);
                return null;
            }

            unit.SetOwnerSeatIndex(ownerSeatIndex);
            unit.currentCity = currentCity;
            if (resetTurnState)
            {
                unit.ResetMovementForTurn();
            }

            bool isActiveTurn = IsCurrentSideOwner(ownerSeatIndex);
            unit.UpdateMoveOutline(resetTurnState && isActiveTurn);
        }

        OwnedSprite owned = spawnedObject.GetComponent<OwnedSprite>();
        if (owned != null)
        {
            owned.SetOwnerSeatIndex(ownerSeatIndex);
        }

        return spawnedObject;
    }

    private bool TryResolveLoadedUnitTypeId(
        string savedUnitTypeId,
        bool loadedModeIsPbp,
        out string resolvedUnitTypeId,
        out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(savedUnitTypeId))
        {
            resolvedUnitTypeId = UnitRegistry.WarriorTypeId;
            return true;
        }

        resolvedUnitTypeId = savedUnitTypeId.Trim();
        if (UnitRegistry.TryGetDefinition(resolvedUnitTypeId, out UnitDefinition definition))
        {
            resolvedUnitTypeId = definition.TypeId;
            return true;
        }

        if (loadedModeIsPbp)
        {
            error = $"PBp load blocked: unknown unitTypeId '{savedUnitTypeId}'.";
            return false;
        }

        Debug.LogWarning($"Unknown unitTypeId '{savedUnitTypeId}' in non-PBp save. Falling back to {UnitRegistry.WarriorTypeId}.");
        resolvedUnitTypeId = UnitRegistry.WarriorTypeId;
        return true;
    }

    private bool TryValidatePbpLoadProtocol(
        GameSave save,
        out int loadedProtocolVersion,
        out int migrationSourceProtocolVersion,
        out string error)
    {
        loadedProtocolVersion = save != null ? save.protocolVersion : 0;
        migrationSourceProtocolVersion = 0;
        error = null;

        if (save == null)
        {
            error = "PBp load blocked: save was null.";
            return false;
        }

        if (loadedProtocolVersion <= 0)
        {
            error =
                $"PBp load blocked: protocolVersion is missing or invalid ({loadedProtocolVersion}), supported={SupportedPbpProtocolVersion}.";
            return false;
        }

        if (loadedProtocolVersion == SupportedPbpProtocolVersion)
        {
            return true;
        }

        if (loadedProtocolVersion == SupportedPbpMigrationProtocolVersion)
        {
            migrationSourceProtocolVersion = loadedProtocolVersion;
            return true;
        }

        error =
            $"PBp load blocked: protocolVersion={loadedProtocolVersion} does not match supported={SupportedPbpProtocolVersion} and is not a supported migration source.";
        return false;
    }

    private static bool TryValidatePbpLoadAppVersion(
        GameSave save,
        out string loadedAppVersion,
        out string error)
    {
        loadedAppVersion = save != null ? save.appVersion : null;
        error = null;

        if (save == null)
        {
            error = "PBp load blocked: save was null.";
            return false;
        }

        // Temporary migration bridge: legacy PbP saves created before appVersion existed
        // are allowed while their protocolVersion remains supported.
        // TODO: Remove this bridge when protocol 3 support is dropped.
        if (string.IsNullOrWhiteSpace(loadedAppVersion))
        {
            loadedAppVersion = null;
            return true;
        }

        loadedAppVersion = loadedAppVersion.Trim();
        if (IsSupportedPbpAppVersion(loadedAppVersion))
        {
            return true;
        }

        error =
            $"PBp load blocked: appVersion={loadedAppVersion} does not match currentAppVersion={CurrentAppVersion}.";
        return false;
    }

    private static int ResolveLoadedCurrentHealthUnits(SavedUnit savedUnit)
    {
        if (savedUnit == null)
        {
            return 0;
        }

        // Prefer explicit scaled units, otherwise migrate whole-number health from older saves.
        if (savedUnit.currentHealthUnits > 0)
        {
            return savedUnit.currentHealthUnits;
        }

        if (savedUnit.currentHealth > 0)
        {
            return CombatValues.FromLegacyWhole(savedUnit.currentHealth);
        }

        return 0;
    }

    private bool LocalIsPlayerOwned()
    {
        if (currentMode == GameMode.PlayByPost)
        {
            return GetLocalIsPlayerOneForGame(currentGameId, out _, out _);
        }

        return localSeat == LocalSeat.Player1;
    }

    private bool GetLocalIsPlayerOneForGame(string gameId, out bool hasSeat, out int seat)
    {
        hasSeat = TryGetLocalSeatIndexForPbp(gameId, out seat);
        return hasSeat && seat == 0;
    }

    private bool TryGetLocalSeatIndexForPbp(string gameId, out int seat)
    {
        seat = 0;

        if (!string.IsNullOrWhiteSpace(gameId) && LocalPlayerSeatStore.TryGetSeat(gameId, out int storedSeat))
        {
            seat = PlayByPostSeatUtility.NormalizeSeatIndex(storedSeat);
            return true;
        }

        return false;
    }

    private void ApplyTypedDisplayNameMetadata(GameSave save)
    {
        if (save == null || currentMode != GameMode.PlayByPost)
        {
            return;
        }

        if (!string.Equals(typedDisplayMetadataGameId, currentGameId, System.StringComparison.Ordinal))
        {
            typedDisplayMetadataGameId = currentGameId;
            knownPlayerOneTypedDisplayName = null;
            knownPlayerTwoTypedDisplayName = null;
            runtimePlayByPostSeatMetadata.Clear();
        }

        save.playerOneTypedDisplayName = knownPlayerOneTypedDisplayName;
        save.playerTwoTypedDisplayName = knownPlayerTwoTypedDisplayName;
    }

    private void UpdateKnownTypedDisplayNames(GameSave save)
    {
        if (save == null ||
            currentMode != GameMode.PlayByPost ||
            string.IsNullOrWhiteSpace(currentGameId))
        {
            typedDisplayMetadataGameId = currentGameId;
            knownPlayerOneTypedDisplayName = null;
            knownPlayerTwoTypedDisplayName = null;
            runtimePlayByPostSeatMetadata.Clear();
            return;
        }

        typedDisplayMetadataGameId = currentGameId;
        int seatCount = ResolvePlayByPostSeatMetadataSeatCount(
            save.seatCount,
            save.seats,
            GetConfiguredPlayByPostSeatCount());
        ReplaceRuntimePlayByPostSeatMetadata(
            BuildNormalizedPlayByPostSeatMetadata(
                save.seats,
                seatCount,
                save.playerOneTypedDisplayName,
                save.playerTwoTypedDisplayName));
        SyncKnownTypedDisplayNamesFromRuntimeSeatMetadata();
    }

    private static string NormalizeTypedDisplayNameMetadataValue(string value)
    {
        string normalized = LocalPlayerProfileStore.NormalizeTypedDisplayName(value);
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string NormalizeClaimedPlayerIdMetadataValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static bool SeatMetadataHasClaimSignal(PlayByPostSeatMetadata seatMetadata)
    {
        if (seatMetadata == null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(seatMetadata.claimedPlayerId) ||
               !string.IsNullOrWhiteSpace(LocalPlayerProfileStore.NormalizeTypedDisplayName(seatMetadata.typedDisplayName));
    }

    private static PlayByPostSeatMetadata CreateDefaultPlayByPostSeatMetadata(int seatIndex)
    {
        return new PlayByPostSeatMetadata
        {
            seatIndex = seatIndex,
            state = PlayByPostSeatUtility.SeatStateUnclaimed,
            claimedPlayerId = string.Empty,
            typedDisplayName = string.Empty
        };
    }

    private static void CopyNormalizedPlayByPostSeatMetadata(
        PlayByPostSeatMetadata target,
        PlayByPostSeatMetadata source,
        int seatIndex)
    {
        if (target == null)
        {
            return;
        }

        target.seatIndex = seatIndex;
        target.state = PlayByPostSeatUtility.SeatStateUnclaimed;
        target.claimedPlayerId = string.Empty;
        target.typedDisplayName = string.Empty;

        if (source == null)
        {
            return;
        }

        target.state = PlayByPostSeatUtility.NormalizeSeatState(source.state);
        target.claimedPlayerId = NormalizeClaimedPlayerIdMetadataValue(source.claimedPlayerId);
        target.typedDisplayName = NormalizeTypedDisplayNameMetadataValue(source.typedDisplayName) ?? string.Empty;
        if (string.Equals(target.state, PlayByPostSeatUtility.SeatStateUnclaimed, System.StringComparison.Ordinal) &&
            SeatMetadataHasClaimSignal(target))
        {
            target.state = PlayByPostSeatUtility.SeatStateActive;
        }
    }

    private static void ApplyLegacyTypedDisplayNameBridge(
        List<PlayByPostSeatMetadata> seats,
        int seatIndex,
        string legacyTypedDisplayName)
    {
        if (seats == null || seatIndex < 0 || seatIndex >= seats.Count)
        {
            return;
        }

        string normalizedLegacyTypedDisplayName = NormalizeTypedDisplayNameMetadataValue(legacyTypedDisplayName);
        if (string.IsNullOrWhiteSpace(normalizedLegacyTypedDisplayName))
        {
            return;
        }

        PlayByPostSeatMetadata seat = seats[seatIndex] ?? CreateDefaultPlayByPostSeatMetadata(seatIndex);
        seats[seatIndex] = seat;
        if (string.IsNullOrWhiteSpace(seat.typedDisplayName))
        {
            seat.typedDisplayName = normalizedLegacyTypedDisplayName;
        }

        if (string.Equals(
                PlayByPostSeatUtility.NormalizeSeatState(seat.state),
                PlayByPostSeatUtility.SeatStateUnclaimed,
                System.StringComparison.Ordinal))
        {
            seat.state = PlayByPostSeatUtility.SeatStateActive;
        }
    }

    private static int ResolvePlayByPostSeatMetadataSeatCount(
        int rawSeatCount,
        List<PlayByPostSeatMetadata> seats,
        int minimumSeatCount = PlayByPostSeatUtility.MinSeatCount)
    {
        int resolvedSeatCount = rawSeatCount > 0
            ? PlayByPostSeatUtility.NormalizeSeatCount(rawSeatCount)
            : PlayByPostSeatUtility.MinSeatCount;
        resolvedSeatCount = Mathf.Max(
            PlayByPostSeatUtility.MinSeatCount,
            PlayByPostSeatUtility.NormalizeSeatCount(minimumSeatCount));

        if (rawSeatCount > 0)
        {
            resolvedSeatCount = Mathf.Max(
                resolvedSeatCount,
                PlayByPostSeatUtility.NormalizeSeatCount(rawSeatCount));
        }

        if (seats != null)
        {
            for (int i = 0; i < seats.Count; i++)
            {
                PlayByPostSeatMetadata seat = seats[i];
                if (seat == null || seat.seatIndex < 0 || seat.seatIndex >= PlayByPostSeatUtility.MaxSeatCount)
                {
                    continue;
                }

                resolvedSeatCount = Mathf.Max(resolvedSeatCount, seat.seatIndex + 1);
            }
        }

        return PlayByPostSeatUtility.NormalizeSeatCount(resolvedSeatCount);
    }

    private static List<PlayByPostSeatMetadata> BuildNormalizedPlayByPostSeatMetadata(
        List<PlayByPostSeatMetadata> sourceSeats,
        int seatCount,
        string legacyPlayerOneTypedDisplayName,
        string legacyPlayerTwoTypedDisplayName)
    {
        int normalizedSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(seatCount);
        List<PlayByPostSeatMetadata> normalizedSeats = new List<PlayByPostSeatMetadata>(normalizedSeatCount);
        for (int seatIndex = 0; seatIndex < normalizedSeatCount; seatIndex++)
        {
            normalizedSeats.Add(CreateDefaultPlayByPostSeatMetadata(seatIndex));
        }

        if (sourceSeats != null)
        {
            for (int i = 0; i < sourceSeats.Count; i++)
            {
                PlayByPostSeatMetadata sourceSeat = sourceSeats[i];
                if (sourceSeat == null ||
                    sourceSeat.seatIndex < 0 ||
                    sourceSeat.seatIndex >= normalizedSeatCount)
                {
                    continue;
                }

                CopyNormalizedPlayByPostSeatMetadata(
                    normalizedSeats[sourceSeat.seatIndex],
                    sourceSeat,
                    sourceSeat.seatIndex);
            }
        }

        ApplyLegacyTypedDisplayNameBridge(normalizedSeats, 0, legacyPlayerOneTypedDisplayName);
        if (normalizedSeatCount > 1)
        {
            ApplyLegacyTypedDisplayNameBridge(normalizedSeats, 1, legacyPlayerTwoTypedDisplayName);
        }

        return normalizedSeats;
    }

    private void ReplaceRuntimePlayByPostSeatMetadata(List<PlayByPostSeatMetadata> normalizedSeats)
    {
        runtimePlayByPostSeatMetadata.Clear();
        if (normalizedSeats == null)
        {
            return;
        }

        for (int i = 0; i < normalizedSeats.Count; i++)
        {
            PlayByPostSeatMetadata seat = normalizedSeats[i];
            PlayByPostSeatMetadata copy = CreateDefaultPlayByPostSeatMetadata(i);
            CopyNormalizedPlayByPostSeatMetadata(copy, seat, i);
            runtimePlayByPostSeatMetadata.Add(copy);
        }
    }

    private void SyncKnownTypedDisplayNamesFromRuntimeSeatMetadata()
    {
        knownPlayerOneTypedDisplayName =
            runtimePlayByPostSeatMetadata.Count > 0
                ? NormalizeTypedDisplayNameMetadataValue(runtimePlayByPostSeatMetadata[0].typedDisplayName)
                : null;
        knownPlayerTwoTypedDisplayName =
            runtimePlayByPostSeatMetadata.Count > 1
                ? NormalizeTypedDisplayNameMetadataValue(runtimePlayByPostSeatMetadata[1].typedDisplayName)
                : null;
    }

    private List<PlayByPostSeatMetadata> BuildSaveSeatMetadata(
        int seatCount,
        bool refreshLocalSeatTypedDisplayName)
    {
        List<PlayByPostSeatMetadata> normalizedSeats = BuildNormalizedPlayByPostSeatMetadata(
            runtimePlayByPostSeatMetadata,
            seatCount,
            knownPlayerOneTypedDisplayName,
            knownPlayerTwoTypedDisplayName);

        if (currentMode != GameMode.PlayByPost || string.IsNullOrWhiteSpace(currentGameId))
        {
            return normalizedSeats;
        }

        if (!TryGetLocalSeatIndexForPbp(currentGameId, out int localSeat) ||
            localSeat < 0 ||
            localSeat >= normalizedSeats.Count)
        {
            return normalizedSeats;
        }

        PlayByPostSeatMetadata localSeatMetadata = normalizedSeats[localSeat];
        string normalizedState = PlayByPostSeatUtility.NormalizeSeatState(localSeatMetadata.state);
        if (string.Equals(normalizedState, PlayByPostSeatUtility.SeatStateResigned, System.StringComparison.Ordinal) ||
            string.Equals(normalizedState, PlayByPostSeatUtility.SeatStateEliminated, System.StringComparison.Ordinal))
        {
            return normalizedSeats;
        }

        LocalPlayerProfileStore.ProfileData profile = LocalPlayerProfileStore.GetOrCreateProfile();
        string normalizedTypedName = NormalizeTypedDisplayNameMetadataValue(profile.TypedDisplayName);
        bool hasExistingClaimSignal = SeatMetadataHasClaimSignal(localSeatMetadata);
        bool hasExistingTypedDisplayName =
            !string.IsNullOrWhiteSpace(NormalizeTypedDisplayNameMetadataValue(localSeatMetadata.typedDisplayName));

        localSeatMetadata.state = PlayByPostSeatUtility.SeatStateActive;
        if (!string.IsNullOrWhiteSpace(profile.PlayerId))
        {
            if (refreshLocalSeatTypedDisplayName || !hasExistingClaimSignal || string.IsNullOrWhiteSpace(localSeatMetadata.claimedPlayerId))
            {
                localSeatMetadata.claimedPlayerId = profile.PlayerId.Trim();
            }
        }

        if (refreshLocalSeatTypedDisplayName)
        {
            if (!string.IsNullOrWhiteSpace(normalizedTypedName))
            {
                localSeatMetadata.typedDisplayName = normalizedTypedName;
            }
        }
        else if ((!hasExistingClaimSignal || !hasExistingTypedDisplayName) &&
                 !string.IsNullOrWhiteSpace(normalizedTypedName))
        {
            localSeatMetadata.typedDisplayName = normalizedTypedName;
        }

        return normalizedSeats;
    }

    private int GetConfiguredPlayByPostSeatCount()
    {
        return PlayByPostSeatUtility.NormalizeSeatCount(configuredPlayByPostSeatCount);
    }

    private int GetRuntimeSeatCount()
    {
        return currentMode == GameMode.PlayByPost
            ? GetConfiguredPlayByPostSeatCount()
            : PlayByPostSeatUtility.MinSeatCount;
    }

    private void EnsureSeatGoldCapacity(int seatCount)
    {
        int normalizedSeatCount = Mathf.Max(PlayByPostSeatUtility.MinSeatCount, seatCount);
        while (seatGold.Count < normalizedSeatCount)
        {
            seatGold.Add(0);
        }

        if (seatGold.Count > normalizedSeatCount)
        {
            seatGold.RemoveRange(normalizedSeatCount, seatGold.Count - normalizedSeatCount);
        }
    }

    private void InitializeSeatGoldForNewGame(int seatCount)
    {
        EnsureSeatGoldCapacity(seatCount);
        for (int seatIndex = 0; seatIndex < seatGold.Count; seatIndex++)
        {
            seatGold[seatIndex] = startingGold;
        }

        SyncLegacyGoldBridge();
    }

    private void SetSeatGoldFromLegacyBridges(int seatCount, int legacyPlayerGold, int legacyAiGold)
    {
        EnsureSeatGoldCapacity(seatCount);
        for (int seatIndex = 0; seatIndex < seatGold.Count; seatIndex++)
        {
            seatGold[seatIndex] = 0;
        }

        if (seatGold.Count > 0)
        {
            seatGold[0] = Mathf.Max(0, legacyPlayerGold);
        }

        if (seatGold.Count > 1)
        {
            seatGold[1] = Mathf.Max(0, legacyAiGold);
        }

        SyncLegacyGoldBridge();
    }

    private void SetSeatGoldStateFromList(List<int> values, int seatCount)
    {
        EnsureSeatGoldCapacity(seatCount);
        for (int seatIndex = 0; seatIndex < seatGold.Count; seatIndex++)
        {
            int value = values != null && seatIndex < values.Count ? values[seatIndex] : 0;
            seatGold[seatIndex] = Mathf.Max(0, value);
        }

        SyncLegacyGoldBridge();
    }

    private List<int> BuildSeatGoldSnapshot(int seatCount)
    {
        int normalizedSeatCount = Mathf.Max(PlayByPostSeatUtility.MinSeatCount, seatCount);
        List<int> snapshot = new List<int>(normalizedSeatCount);
        for (int seatIndex = 0; seatIndex < normalizedSeatCount; seatIndex++)
        {
            snapshot.Add(GetGoldForSeat(seatIndex));
        }

        return snapshot;
    }

    private void SyncLegacyGoldBridge()
    {
        playerGold = seatGold.Count > 0 ? seatGold[0] : 0;
        aiGold = seatGold.Count > 1 ? seatGold[1] : 0;
    }

    private void SyncLegacyTurnOwnerBridge()
    {
        isPlayerTurn = currentTurnSeatIndex == 0;
    }

    private void SetCurrentTurnSeatIndexForRuntime(int seatIndex, int seatCount)
    {
        currentTurnSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(seatIndex, seatCount);
        SyncLegacyTurnOwnerBridge();
    }

    private int GetAuthoritativeCurrentTurnSeatIndex()
    {
        return currentMode == GameMode.PlayByPost
            ? PlayByPostSeatUtility.NormalizeSeatIndex(currentTurnSeatIndex, GetConfiguredPlayByPostSeatCount())
            : (isPlayerTurn ? 0 : 1);
    }

    public int GetViewerSeatIndexForRuntime()
    {
        if (currentMode == GameMode.PlayByPost)
        {
            if (string.IsNullOrWhiteSpace(currentGameId))
            {
                return 0;
            }

            if (TryGetLocalSeatIndexForPbp(currentGameId, out int localSeat))
            {
                return localSeat;
            }

            return 0;
        }

        return 0;
    }

    private static int ResolveCurrentTurnSeatIndex(GameSave save, int seatCount)
    {
        if (save == null)
        {
            return 0;
        }

        if (save.protocolVersion >= SupportedPbpProtocolVersion && save.currentTurnSeatIndex >= 0)
        {
            return PlayByPostSeatUtility.NormalizeSeatIndex(save.currentTurnSeatIndex, seatCount);
        }

        return save.isPlayerTurn ? 0 : 1;
    }

    private static bool IsEligiblePlayByPostSeatForTurnProgression(PlayByPostSeatMetadata seatMetadata)
    {
        string normalizedState = PlayByPostSeatUtility.NormalizeSeatState(
            seatMetadata != null ? seatMetadata.state : null);
        return !string.Equals(normalizedState, PlayByPostSeatUtility.SeatStateResigned, System.StringComparison.Ordinal) &&
               !string.Equals(normalizedState, PlayByPostSeatUtility.SeatStateEliminated, System.StringComparison.Ordinal);
    }

    private static int CountEligiblePlayByPostSeats(List<PlayByPostSeatMetadata> normalizedSeats, int seatCount)
    {
        int normalizedSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(seatCount);
        int eligibleSeats = 0;
        for (int seatIndex = 0; seatIndex < normalizedSeatCount; seatIndex++)
        {
            PlayByPostSeatMetadata seatMetadata =
                normalizedSeats != null && seatIndex < normalizedSeats.Count
                    ? normalizedSeats[seatIndex]
                    : null;
            if (IsEligiblePlayByPostSeatForTurnProgression(seatMetadata))
            {
                eligibleSeats++;
            }
        }

        return eligibleSeats;
    }

    private static int FindNextEligiblePlayByPostSeatIndex(
        List<PlayByPostSeatMetadata> normalizedSeats,
        int seatCount,
        int afterSeatIndex,
        out bool wrapped)
    {
        int normalizedSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(seatCount);
        int normalizedAfterSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(afterSeatIndex, normalizedSeatCount);
        wrapped = false;

        for (int step = 1; step <= normalizedSeatCount; step++)
        {
            int candidateSeatIndex = (normalizedAfterSeatIndex + step) % normalizedSeatCount;
            PlayByPostSeatMetadata seatMetadata =
                normalizedSeats != null && candidateSeatIndex < normalizedSeats.Count
                    ? normalizedSeats[candidateSeatIndex]
                    : null;
            if (!IsEligiblePlayByPostSeatForTurnProgression(seatMetadata))
            {
                continue;
            }

            wrapped = candidateSeatIndex <= normalizedAfterSeatIndex;
            return candidateSeatIndex;
        }

        return -1;
    }

    private static void ResolveAdvancedPlayByPostTurnState(
        GameSave save,
        List<PlayByPostSeatMetadata> normalizedSeats,
        int seatCount,
        int activeSeatIndex,
        out int nextSeatIndex,
        out int nextTurnNumber,
        out bool wrapped,
        out int eligibleSeatCount)
    {
        int normalizedSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(seatCount);
        int normalizedActiveSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(activeSeatIndex, normalizedSeatCount);
        eligibleSeatCount = CountEligiblePlayByPostSeats(normalizedSeats, normalizedSeatCount);
        nextSeatIndex = FindNextEligiblePlayByPostSeatIndex(
            normalizedSeats,
            normalizedSeatCount,
            normalizedActiveSeatIndex,
            out wrapped);
        if (nextSeatIndex < 0)
        {
            nextSeatIndex = normalizedActiveSeatIndex;
            wrapped = false;
        }

        nextTurnNumber = Mathf.Max(0, save != null ? save.turnNumber : 0);
        if (nextSeatIndex != normalizedActiveSeatIndex && wrapped)
        {
            nextTurnNumber++;
        }
    }

    private static int ResolveOwnerSeatIndex(int serializedSeatIndex, bool legacyIsPlayerOwned, int seatCount)
    {
        if (serializedSeatIndex >= 0)
        {
            return PlayByPostSeatUtility.NormalizeSeatIndex(serializedSeatIndex, seatCount);
        }

        return legacyIsPlayerOwned ? 0 : 1;
    }

    private static List<int> ResolveSeenSeatIndices(SavedTile tile)
    {
        List<int> resolved = new List<int>();
        if (tile == null)
        {
            return resolved;
        }

        if (tile.seenSeatIndices != null && tile.seenSeatIndices.Count > 0)
        {
            for (int i = 0; i < tile.seenSeatIndices.Count; i++)
            {
                int seatIndex = tile.seenSeatIndices[i];
                if (seatIndex >= 0 && !resolved.Contains(seatIndex))
                {
                    resolved.Add(seatIndex);
                }
            }

            resolved.Sort();
            return resolved;
        }

        if (tile.playerSeen)
        {
            resolved.Add(0);
        }

        if (tile.opponentSeen)
        {
            resolved.Add(1);
        }

        return resolved;
    }

    private static List<int> ResolveSeatGold(GameSave save, int seatCount)
    {
        List<int> resolved = new List<int>(Mathf.Max(PlayByPostSeatUtility.MinSeatCount, seatCount));
        for (int seatIndex = 0; seatIndex < Mathf.Max(PlayByPostSeatUtility.MinSeatCount, seatCount); seatIndex++)
        {
            int value = 0;
            if (save != null && save.seatGold != null && seatIndex < save.seatGold.Count)
            {
                value = save.seatGold[seatIndex];
            }
            else if (seatIndex == 0 && save != null)
            {
                value = save.playerGold;
            }
            else if (seatIndex == 1 && save != null)
            {
                value = save.aiGold;
            }

            resolved.Add(Mathf.Max(0, value));
        }

        return resolved;
    }

    public bool IsCurrentSideOwner(int ownerSeatIndex)
    {
        if (currentMode == GameMode.PlayByPost)
        {
            return CanLocalPlayerIssueCommands() &&
                   currentTurnSeatIndex == PlayByPostSeatUtility.NormalizeSeatIndex(ownerSeatIndex, GetConfiguredPlayByPostSeatCount());
        }

        return isPlayerTurn && ownerSeatIndex == 0;
    }

    public int GetGoldForSeat(int seatIndex)
    {
        int normalizedSeatIndex = Mathf.Max(0, seatIndex);
        EnsureSeatGoldCapacity(Mathf.Max(GetRuntimeSeatCount(), normalizedSeatIndex + 1));
        return seatGold[normalizedSeatIndex];
    }

    public int GetDisplayedGoldForUi()
    {
        return currentMode == GameMode.PlayByPost
            ? GetGoldForSeat(GetViewerSeatIndexForRuntime())
            : (isPlayerTurn ? playerGold : aiGold);
    }

    public string GetLocalPlayByPostSeatLabelForUi()
    {
        if (currentMode != GameMode.PlayByPost)
        {
            return GetCurrentSideName();
        }

        return PlayByPostSeatUtility.BuildPlayerLabel(GetViewerSeatIndexForRuntime());
    }

    public string GetCurrentPlayByPostTurnOwnerLabelForUi()
    {
        if (currentMode != GameMode.PlayByPost)
        {
            return GetCurrentSideName();
        }

        return ResolvePlayByPostTurnOwnerLabelForUi(GetAuthoritativeCurrentTurnSeatIndex());
    }

    // Used only for the post-submit HUD override before the next authoritative
    // fetched snapshot is applied locally. This predicts the next waiting seat
    // from the local seat handoff, then normalizes past resigned/eliminated seats.
    public string GetPredictedPostSubmitPlayByPostTurnOwnerLabelForUi()
    {
        if (currentMode != GameMode.PlayByPost || string.IsNullOrWhiteSpace(currentGameId))
        {
            return string.Empty;
        }

        if (!TryGetLocalSeatIndexForPbp(currentGameId, out int localSeat))
        {
            return string.Empty;
        }

        int seatCount = GetConfiguredPlayByPostSeatCount();
        return ResolvePlayByPostTurnOwnerLabelForUi((localSeat + 1) % seatCount);
    }

    public string GetCurrentPlayByPostWaitingTextForUi()
    {
        if (currentMode != GameMode.PlayByPost)
        {
            return string.Empty;
        }

        return BuildPlayByPostWaitingTextForUi(GetAuthoritativeCurrentTurnSeatIndex());
    }

    private string ResolvePlayByPostTurnOwnerLabelForUi(int seatIndex)
    {
        int seatCount = GetConfiguredPlayByPostSeatCount();
        int waitingSeatIndex = PlayByPostSeatUtility.ResolveEffectiveWaitingSeatIndex(
            runtimePlayByPostSeatMetadata,
            seatIndex,
            seatCount);
        if (waitingSeatIndex < 0)
        {
            return string.Empty;
        }

        string typedDisplayName =
            waitingSeatIndex < runtimePlayByPostSeatMetadata.Count
                ? runtimePlayByPostSeatMetadata[waitingSeatIndex].typedDisplayName
                : null;
        return ResolvePlayByPostSeatDisplayNameForUi(waitingSeatIndex, typedDisplayName);
    }

    private string BuildPlayByPostWaitingTextForUi(int seatIndex)
    {
        int seatCount = GetConfiguredPlayByPostSeatCount();
        int waitingSeatIndex = PlayByPostSeatUtility.ResolveEffectiveWaitingSeatIndex(
            runtimePlayByPostSeatMetadata,
            seatIndex,
            seatCount);
        if (waitingSeatIndex < 0)
        {
            return "Waiting";
        }

        PlayByPostSeatMetadata waitingSeatMetadata =
            waitingSeatIndex < runtimePlayByPostSeatMetadata.Count
                ? runtimePlayByPostSeatMetadata[waitingSeatIndex]
                : null;
        string normalizedSeatState = PlayByPostSeatUtility.NormalizeSeatState(
            waitingSeatMetadata != null ? waitingSeatMetadata.state : null);
        if (string.Equals(normalizedSeatState, PlayByPostSeatUtility.SeatStateUnclaimed, System.StringComparison.Ordinal))
        {
            return $"Waiting for {PlayByPostSeatUtility.BuildPlayerLabel(waitingSeatIndex)} to join";
        }

        string typedDisplayName =
            waitingSeatIndex < runtimePlayByPostSeatMetadata.Count
                ? runtimePlayByPostSeatMetadata[waitingSeatIndex].typedDisplayName
                : null;
        return $"Waiting for {ResolvePlayByPostSeatDisplayNameForUi(waitingSeatIndex, typedDisplayName)}";
    }

    private static string ResolvePlayByPostSeatDisplayNameForUi(int seatIndex, string typedDisplayName)
    {
        return PlayByPostSeatUtility.ResolveSeatDisplayNameOrFallback(seatIndex, typedDisplayName);
    }

    private void ApplyPlayByPostSeatMetadata(GameSave save, bool refreshLocalSeatTypedDisplayName = false)
    {
        if (save == null)
        {
            return;
        }

        int seatCount = currentMode == GameMode.PlayByPost
            ? GetConfiguredPlayByPostSeatCount()
            : PlayByPostSeatUtility.MinSeatCount;
        int currentTurnSeatIndex = ResolveCurrentTurnSeatIndex(save, seatCount);
        save.seatCount = seatCount;
        save.currentTurnSeatIndex = currentTurnSeatIndex;
        save.isPlayerTurn = currentTurnSeatIndex == 0;
        save.transportSeq = ComputeTransportSeq(save.turnNumber, currentTurnSeatIndex, seatCount);

        save.seats = BuildSaveSeatMetadata(seatCount, refreshLocalSeatTypedDisplayName);
        save.playerOneTypedDisplayName = save.seats.Count > 0
            ? NormalizeTypedDisplayNameMetadataValue(save.seats[0].typedDisplayName)
            : null;
        save.playerTwoTypedDisplayName = save.seats.Count > 1
            ? NormalizeTypedDisplayNameMetadataValue(save.seats[1].typedDisplayName)
            : null;
    }

    public string GetCurrentPlayByPostOpponentTypedDisplayName()
    {
        if (currentMode != GameMode.PlayByPost ||
            string.IsNullOrWhiteSpace(currentGameId) ||
            !string.Equals(typedDisplayMetadataGameId, currentGameId, System.StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (!TryGetLocalSeatIndexForPbp(currentGameId, out int localSeat) ||
            (localSeat != 0 && localSeat != 1))
        {
            return string.Empty;
        }

        return localSeat == 0
            ? (knownPlayerTwoTypedDisplayName ?? string.Empty)
            : (knownPlayerOneTypedDisplayName ?? string.Empty);
    }

    private bool EnsurePlayByPostControlReadiness()
    {
        if (currentMode != GameMode.PlayByPost)
        {
            pbpControlReadinessReady = false;
            return true;
        }

        bool wasReady = pbpControlReadinessReady;

        if (string.IsNullOrWhiteSpace(currentGameId))
        {
            string prefsGameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(prefsGameId))
            {
                SetCurrentGameId(prefsGameId);
            }
        }

        bool hasGameId = !string.IsNullOrWhiteSpace(currentGameId);
        bool hasSeat = hasGameId && TryGetLocalSeatIndexForPbp(currentGameId, out _);

        // Joiner self-heal: if currentGameId points to a stale/mismatched id without a seat,
        // but prefs has the active PBp id with a valid seat, adopt that id in-session.
        if (!hasSeat && hasGameId)
        {
            string prefsGameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(prefsGameId) &&
                !string.Equals(prefsGameId, currentGameId, System.StringComparison.Ordinal) &&
                TryGetLocalSeatIndexForPbp(prefsGameId, out _))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                string adoptionKey = $"{currentGameId}->{prefsGameId}";
                if (!string.Equals(lastPbpSeatAdoptionLogKey, adoptionKey, System.StringComparison.Ordinal))
                {
                    lastPbpSeatAdoptionLogKey = adoptionKey;
                    Debug.Log(
                        $"[PBpSeat] Adopted prefs gameId due to missing seat for currentGameId (from={currentGameId}, to={prefsGameId}).");
                }
#endif
                SetCurrentGameId(prefsGameId);
                hasGameId = true;
                hasSeat = TryGetLocalSeatIndexForPbp(currentGameId, out _);
            }
        }

        if (!hasSeat &&
            hasGameId &&
            !string.IsNullOrWhiteSpace(pbpCreatorBootstrapGameId) &&
            string.Equals(currentGameId, pbpCreatorBootstrapGameId, System.StringComparison.Ordinal))
        {
            LocalPlayerSeatStore.SetSeat(currentGameId, 0);
            hasSeat = TryGetLocalSeatIndexForPbp(currentGameId, out _);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (hasSeat)
            {
                Debug.Log($"[PBpSeat] Initialized missing creator seat (gameId={currentGameId}, seat=0).");
            }
#endif
        }

        pbpControlReadinessReady = hasGameId && hasSeat;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!pbpControlReadinessReady)
        {
            string reason = !hasGameId ? "gameId_missing" : "seat_missing";
            string gameIdText = hasGameId ? currentGameId : "<none>";
            string blockKey = $"{gameIdText}|{reason}";
            if (!string.Equals(lastPbpControlReadinessBlockKey, blockKey, System.StringComparison.Ordinal))
            {
                lastPbpControlReadinessBlockKey = blockKey;
                Debug.LogWarning($"[PBpSeat] Blocking local PBp control ({reason}, gameId={gameIdText}).");
            }
        }
        else
        {
            lastPbpControlReadinessBlockKey = null;
        }
#endif

        if (!wasReady && pbpControlReadinessReady)
        {
            if (UnitSelectionManager.Instance != null)
            {
                UnitSelectionManager.Instance.ClearSelection();
                UnitSelectionManager.Instance.RefreshMoveOutlinesForCurrentTurn();
            }

            if (TileHoverManager.Instance != null)
            {
                TileHoverManager.Instance.ClearSelection();
            }

            RefreshEndTurnButtonInteractable(force: true);
        }

        return pbpControlReadinessReady;
    }

    private bool GetViewerIsPlayerOwned()
    {
        return GetViewerSeatIndexForRuntime() == 0;
    }

    public bool IsCurrentSideOwner(bool isPlayerOwned)
    {
        if (currentMode == GameMode.PlayByPost)
        {
            if (!TryGetLocalSeatIndexForPbp(currentGameId, out int localSeat))
                return false;

            int bridgedSeatIndex = isPlayerOwned ? 0 : 1;
            return currentTurnSeatIndex == localSeat && bridgedSeatIndex == localSeat;
        }

        // Vs AI: only player-owned units/cities are controllable during the player turn
        return isPlayerTurn && isPlayerOwned;
    }

    public bool CanLocalPlayerIssueCommands()
    {
        if (currentMode != GameMode.PlayByPost)
            return true;

        if (!EnsurePlayByPostControlReadiness())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogPbpInputDeniedIfNeeded("readiness_not_ready");
#endif
            return false;
        }

        if (isPlayByPostWaitingForExport)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogPbpInputDeniedIfNeeded("waiting_for_export");
#endif
            return false;
        }

        bool localTurnMatchesSeat = LocalTurnMatchesSeat();
        if (!localTurnMatchesSeat)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogPbpInputDeniedIfNeeded("seat_turn_mismatch");
#endif
            return false;
        }

        return true;
    }

    private bool LocalTurnMatchesSeat()
    {
        if (!TryGetLocalSeatIndexForPbp(currentGameId, out int seat))
            return false;

        return currentTurnSeatIndex == seat;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void LogPbpInputDeniedIfNeeded(string gate)
    {
        if (!PbpDebugSettingsLoader.EnableInputLogs)
            return;

        float now = Time.realtimeSinceStartup;
        if ((now - lastPbpInputDeniedLogTime) < PbpInputDeniedLogCooldownSeconds)
            return;

        lastPbpInputDeniedLogTime = now;
        bool seatExists = TryGetLocalSeatIndexForPbp(currentGameId, out int seatIndex);
        bool localTurnMatchesSeat = LocalTurnMatchesSeat();
        string gameIdForLog = string.IsNullOrWhiteSpace(currentGameId) ? "<none>" : currentGameId;
        string seatIndexForLog = seatExists ? seatIndex.ToString() : "<none>";
        Debug.Log(
            $"[PBpInputDenied] gate={gate} currentGameId={gameIdForLog} isPlayByPostWaitingForExport={isPlayByPostWaitingForExport} isPlayByPostFetchInProgress={isPlayByPostFetchInProgress} playByPostLastFetchWasNoTurn={playByPostLastFetchWasNoTurn} lastAppliedTurnNumberForPolling={lastAppliedTurnNumberForPolling} LocalTurnMatchesSeat={localTurnMatchesSeat} pbpControlReadinessReady={pbpControlReadinessReady} seatExists={seatExists} seatIndex={seatIndexForLog} isPlayerTurn={isPlayerTurn}");
    }

    public void LogPbpSelectionGateIfNeeded(string gate, bool pointerOverUi, Unit unit = null, string reason = null)
    {
        if (currentMode != GameMode.PlayByPost)
            return;

        if (!PbpDebugSettingsLoader.EnableInputLogs)
            return;

        float now = Time.realtimeSinceStartup;
        if ((now - lastPbpSelectionGateLogTime) < PbpSelectionGateLogCooldownSeconds)
            return;

        lastPbpSelectionGateLogTime = now;
        string gameIdForLog = string.IsNullOrWhiteSpace(currentGameId) ? "<none>" : currentGameId;
        string reasonForLog = string.IsNullOrWhiteSpace(reason) ? "<none>" : reason;
        string unitNameForLog = unit != null ? unit.name : "<none>";
        string unitIdForLog = unit != null ? unit.GetEntityId().ToString() : "<none>";
        string unitOwnerForLog = unit != null ? unit.isPlayerOwned.ToString() : "<none>";
        Debug.Log(
            $"[PBpSelectionGate] gate={gate} reason={reasonForLog} currentGameId={gameIdForLog} isPlayByPostFetchInProgress={isPlayByPostFetchInProgress} isPlayByPostWaitingForExport={isPlayByPostWaitingForExport} pbpControlReadinessReady={pbpControlReadinessReady} pointerOverUi={pointerOverUi} unitName={unitNameForLog} unitId={unitIdForLog} unitIsPlayerOwned={unitOwnerForLog}");
    }
#endif

    public bool CanControlUnit(Unit unit)
    {
        if (unit == null || gameOver)
            return false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsAIVsAIDebugModeActive())
            return false;
#endif

        if (currentMode == GameMode.PlayByPost && !CanLocalPlayerIssueCommands())
            return false;

        return IsHumanTurn() && IsCurrentSideOwner(unit.ownerSeatIndex);
    }

    public bool CanControlCity(City city)
    {
        if (city == null || gameOver)
            return false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsAIVsAIDebugModeActive())
            return false;
#endif

        if (currentMode == GameMode.PlayByPost && !CanLocalPlayerIssueCommands())
            return false;

        return IsHumanTurn() && IsCurrentSideOwner(city.ownerSeatIndex);
    }

    public string GetCurrentSideName()
    {
        if (currentMode == GameMode.PlayByPost)
        {
            return GetLocalPlayByPostSeatLabelForUi();
        }

        return isPlayerTurn ? "Player" : "AI";
    }

    public void SetGameMode(GameMode mode)
    {
        if (currentMode != GameMode.None || gameOver)
            return;

        GameMode previousMode = currentMode;
        currentMode = mode;

        if (previousMode != currentMode)
        {
            hasCachedEndTurnButtonInteractable = false;
        }

        if (currentMode != GameMode.PlayByPost)
        {
            ResetPlayByPostRuntimeState();
        }

        Time.timeScale = 1f;
        RecalculatePlayerVisibility();

        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.RefreshMoveOutlinesForCurrentTurn();
        }

        RefreshEndTurnButtonInteractable(force: true);
    }

    private void ResetPlayByPostRuntimeState()
    {
        isPlayByPostWaitingForExport = false;
        isPlayByPostFetchInProgress = false;
        playByPostResignationSubmitInFlight = false;
        playByPostLastFetchWasNoTurn = false;
        pbpControlReadinessReady = false;
        pbpCreatorFirstRemoteSubmitPending = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        lastPbpControlReadinessBlockKey = null;
        lastPbpSeatAdoptionLogKey = null;
        lastPbpInputDeniedLogTime = -999f;
        lastPbpSelectionGateLogTime = -999f;
#endif
        typedDisplayMetadataGameId = null;
        knownPlayerOneTypedDisplayName = null;
        knownPlayerTwoTypedDisplayName = null;
        runtimePlayByPostSeatMetadata.Clear();
        ResetPbpEndgameRuntimeState();

        if (playByPostPollRoutine != null)
        {
            StopCoroutine(playByPostPollRoutine);
            playByPostPollRoutine = null;
        }

    }

    private void ResetPbpEndgameRuntimeState()
    {
        pbpEndgamePrimaryAction = PbpEndgamePrimaryAction.None;
        pbpEndgameLocalWinner = false;
        pbpEndgameSubmitPending = false;
        pbpEndgameSubmitSucceeded = false;
        pbpEndgameSubmitPayloadCached = false;
        pbpEndgameCachedExportJson = null;
        pbpEndgameCachedTransportSeq = 0;
        pbpEndgameCachedExportTurnNumber = 0;
        pbpEndgameCachedExportIsPlayerTurn = false;
        pbpEndgameCachedExportGameId = null;
        pbpWinnerSeatIndex = -1;
    }

    private void ResetGameOverUiState()
    {
        gameOverUiTitle = string.Empty;
        gameOverUiMessage = string.Empty;
        gameOverUiPrimaryButtonLabel = DefaultGameOverPrimaryButtonLabel;
        gameOverUiPrimaryButtonInteractable = true;
        gameOverUiPrimaryAction = GameOverPrimaryUiAction.ReplayCurrentMode;
        gameOverUiSecondaryButtonLabel = string.Empty;
        gameOverUiSecondaryButtonVisible = false;
        gameOverUiSecondaryButtonInteractable = false;
        gameOverUiSecondaryAction = GameOverSecondaryAction.None;
        gameOverUiTertiaryButtonLabel = string.Empty;
        gameOverUiTertiaryButtonVisible = false;
        gameOverUiTertiaryButtonInteractable = false;
        gameOverUiTertiaryAction = GameOverTertiaryAction.None;
        aiVsAiPausedTournamentSummaryVisible = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        gameOverUiRepeatAIVsAISimulationSettings = AIVsAIBatchRunController.GetDefaultSimulationSettings();
        gameOverUiRepeatSideARecruitVariant = AIRecruitVariant.Default;
        gameOverUiRepeatSideBRecruitVariant = AIRecruitVariant.Default;
        gameOverUiRepeatSideAFeatures = AILocalDecisionFeatures.None;
        gameOverUiRepeatSideBFeatures = AILocalDecisionFeatures.None;
        gameOverUiRepeatSideAProfile = AIDebugProfile.Baseline;
        gameOverUiRepeatSideBProfile = AIDebugProfile.Baseline;
        aiVsAiCompletedTournamentAutoRestartPending = false;
        aiVsAiCompletedTournamentAutoRestartMessage = string.Empty;
#endif
    }

    private void SetGameOverUiTitle(string title)
    {
        gameOverUiTitle = string.IsNullOrWhiteSpace(title) ? DefaultGameOverTitle : title.Trim();
    }

    private void SetGameOverUiMessage(string message)
    {
        gameOverUiMessage = string.IsNullOrWhiteSpace(message) ? DefaultGameOverMessage : message.Trim();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveTelemetrySink();
    }

    void OnEnable()
    {
        PlayByPostSubmitResult += OnPlayByPostSubmitResultForEndgame;
    }

    void OnDisable()
    {
        PlayByPostSubmitResult -= OnPlayByPostSubmitResultForEndgame;
    }

    void Start()
    {
        ResetPlayByPostRuntimeState();
        ResetGameOverUiState();
        ResolveTurnTransport();
        lastAppliedTurnNumberForPolling = turnNumber;
        EnsureEventSystemExists();
        GameplayInputOrchestrator.ResetTransientInputState();
        TryStartGameplayMusic();
        RefreshEndTurnButtonInteractable(force: true);
        StartCoroutine(StartupSequence());
    }

    void Update()
    {
        if (gameOver)
            return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsAIVsAIDebugModeActive() &&
            currentMode == GameMode.VsAI &&
            aiVsAiDebugRoutine == null &&
            !aiVsAiDebugRestartPending)
        {
            StartAIVsAIDebugLoopIfNeeded();
        }
#endif

        RecordHumanInputIfAny();
    }

    private void RefreshEndTurnButtonInteractable(bool force = false)
    {
        if (endTurnButton == null)
            return;

        bool shouldBeInteractable = CanAdvanceTurn();
        if (!force &&
            hasCachedEndTurnButtonInteractable &&
            cachedEndTurnButtonInteractable == shouldBeInteractable)
        {
            return;
        }

        endTurnButton.interactable = shouldBeInteractable;
        cachedEndTurnButtonInteractable = shouldBeInteractable;
        hasCachedEndTurnButtonInteractable = true;
    }

    private void RecordHumanInputIfAny()
    {
        if (!IsHumanTurn())
            return;

        // This is only used to prevent surprise auto-end in Vs AI.
        if (currentMode != GameMode.VsAI || !isPlayerTurn)
            return;

        bool input = GameplayInputOrchestrator.TryGetSnapshot(out GameplayInputOrchestrator.FrameSnapshot snapshot) &&
                     snapshot.AnyHumanInputThisFrame;

        if (input)
        {
            lastHumanInputUnscaledTime = Time.unscaledTime;
        }
    }

    // 🚩 This is what the UI Button will call
    public void OnEndTurnButtonPressed()
    {
        if (!CanAdvanceTurn())
        {
            // Ignore clicks if it's not the current human's turn
            RefreshEndTurnButtonInteractable(force: true);
            return;
        }

        EndCurrentTurn(true);
    }

    public void OnPlayAgainButtonPressed()
    {
        if (gameOverUiPrimaryAction == GameOverPrimaryUiAction.CopyGameOverMessage)
        {
            string textToCopy = GameOverUiMessage;
            if (!string.IsNullOrWhiteSpace(textToCopy))
            {
                ClipboardUtility.TryCopy(textToCopy);
            }
            return;
        }

        if (currentMode == GameMode.PlayByPost && gameOver)
        {
            if (pbpEndgamePrimaryAction == PbpEndgamePrimaryAction.RetrySubmit)
            {
                TryStartPbpEndgameAutoSubmit();
                return;
            }

            if (pbpEndgamePrimaryAction == PbpEndgamePrimaryAction.BackToMultiplayer)
            {
                ReturnToMultiplayer();
                return;
            }

            if (pbpEndgamePrimaryAction == PbpEndgamePrimaryAction.BackAndArchive)
            {
                ReturnToMultiplayerAndArchiveLocalPbpCopy();
                return;
            }

            return;
        }

        // Preserve the mode (VsAI / PlayByPost) for the next game.
        GameModeSelection.SetPendingMode(currentMode);
        MapSizeSelection.SetPending(GetCurrentMapSizePreset());

        Time.timeScale = 1f;
        var currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }

    public void OnGameOverSecondaryButtonPressed()
    {
        if (!gameOverUiSecondaryButtonVisible || !gameOverUiSecondaryButtonInteractable)
        {
            return;
        }

        if (gameOverUiSecondaryAction == GameOverSecondaryAction.BackToAIVsAISettings)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MainMenuController.SetPendingAIVsAISettingsReturn(new MainMenuController.PendingAIVsAISettingsReturn
            {
                mapSizePreset = GetCurrentMapSizePreset(),
                recruitVariant = aiRecruitVariant,
                storeSnapshotHistory = MatchSnapshotHistorySettings.IsEnabled(currentGameId),
                enableAIVsAIDebugMode = true,
                aiVsAiBatchSpeedPreset = aiVsAiBatchSpeedPreset,
                sideARecruitVariant = gameOverUiRepeatSideARecruitVariant,
                sideBRecruitVariant = gameOverUiRepeatSideBRecruitVariant,
                sideAFeatures = gameOverUiRepeatSideAFeatures,
                sideBFeatures = gameOverUiRepeatSideBFeatures,
                sideAProfile = gameOverUiRepeatSideAProfile,
                sideBProfile = gameOverUiRepeatSideBProfile,
                aiVsAiSimulationSettings = gameOverUiRepeatAIVsAISimulationSettings
            });
#endif
            Time.timeScale = 1f;
            SceneManager.LoadScene(MainMenuSceneName);
            return;
        }

        if (gameOverUiSecondaryAction != GameOverSecondaryAction.MainMenu)
        {
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(MainMenuSceneName);
    }

    public void OnGameOverTertiaryButtonPressed()
    {
        if (!gameOverUiTertiaryButtonVisible || !gameOverUiTertiaryButtonInteractable)
        {
            return;
        }

        if (gameOverUiTertiaryAction == GameOverTertiaryAction.ResumeAIVsAIBatch)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            aiVsAiPausedTournamentSummaryVisible = false;
            aiVsAiDebugPaused = false;
            AIVsAIBatchRunController.SetPaused(false);
            ResetGameOverUiState();
            if (gameOver && HasAIVsAIBatchRunActiveForUi())
            {
                QueueAIVsAIDebugMatchRestart();
                return;
            }
            RefreshEndTurnButtonInteractable(force: true);
#endif
            return;
        }

        if (gameOverUiTertiaryAction != GameOverTertiaryAction.RestartAIVsAIBatch)
        {
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        SaveLoadRequest.ClearPending();
        GameModeSelection.SetPendingMode(GameMode.VsAI);
        MapSizeSelection.SetPending(GetCurrentMapSizePreset());
        AIRecruitVariantSelection.SetPending(aiRecruitVariant);
        SnapshotHistorySelection.SetPending(MatchSnapshotHistorySettings.IsEnabled(currentGameId));
        AIVsAIBatchRunController.SetPendingSimulationSettings(gameOverUiRepeatAIVsAISimulationSettings);
        QueuePendingAIVsAIDebugSelectionForSimulation(
            gameOverUiRepeatAIVsAISimulationSettings,
            gameOverUiRepeatSideARecruitVariant,
            gameOverUiRepeatSideBRecruitVariant,
            gameOverUiRepeatSideAFeatures,
            gameOverUiRepeatSideBFeatures,
            gameOverUiRepeatSideAProfile,
            gameOverUiRepeatSideBProfile);

        Time.timeScale = 1f;
        CameraController.ClearPendingRestoreState();
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
#endif
    }

    private void SetGameOverPrimaryButtonState(string label, bool interactable, PbpEndgamePrimaryAction action)
    {
        pbpEndgamePrimaryAction = action;
        gameOverUiPrimaryAction = GameOverPrimaryUiAction.ReplayCurrentMode;
        string resolvedLabel = string.IsNullOrWhiteSpace(label) ? DefaultGameOverPrimaryButtonLabel : label;
        gameOverUiPrimaryButtonLabel = resolvedLabel;
        gameOverUiPrimaryButtonInteractable = interactable;
    }

    private void SetGameOverPrimaryCopyTextButtonState(string label, bool interactable)
    {
        pbpEndgamePrimaryAction = PbpEndgamePrimaryAction.None;
        gameOverUiPrimaryAction = GameOverPrimaryUiAction.CopyGameOverMessage;
        gameOverUiPrimaryButtonLabel = string.IsNullOrWhiteSpace(label) ? "Copy Text" : label.Trim();
        gameOverUiPrimaryButtonInteractable = interactable;
    }

    private void SetGameOverSecondaryButtonState(string label, bool visible, bool interactable, GameOverSecondaryAction action)
    {
        gameOverUiSecondaryButtonLabel = visible && !string.IsNullOrWhiteSpace(label) ? label.Trim() : string.Empty;
        gameOverUiSecondaryButtonVisible = visible;
        gameOverUiSecondaryButtonInteractable = visible && interactable;
        gameOverUiSecondaryAction = visible ? action : GameOverSecondaryAction.None;
    }

    private void SetGameOverTertiaryButtonState(string label, bool visible, bool interactable, GameOverTertiaryAction action)
    {
        gameOverUiTertiaryButtonLabel = visible && !string.IsNullOrWhiteSpace(label) ? label.Trim() : string.Empty;
        gameOverUiTertiaryButtonVisible = visible;
        gameOverUiTertiaryButtonInteractable = visible && interactable;
        gameOverUiTertiaryAction = visible ? action : GameOverTertiaryAction.None;
    }

    private void ShowGameOverPopup(string message, bool writeLog = true)
    {
        SetGameOverUiMessage(message);
        if (currentMode != GameMode.PlayByPost)
        {
            if (gameOverUiPrimaryAction == GameOverPrimaryUiAction.ReplayCurrentMode)
            {
                gameOverUiPrimaryButtonLabel = DefaultGameOverPrimaryButtonLabel;
                gameOverUiPrimaryButtonInteractable = true;
            }
        }
        else if (pbpEndgamePrimaryAction == PbpEndgamePrimaryAction.None)
        {
            gameOverUiPrimaryButtonLabel = DefaultGameOverPrimaryButtonLabel;
            gameOverUiPrimaryButtonInteractable = false;
        }

        if (writeLog)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("Game Over: " + message);
#endif
        }
    }

    private bool TryShowPbpVersionMismatchInGamePopup(int loadedProtocolVersion, string gameId, string debugSource)
    {
        if (loadedProtocolVersion <= SupportedPbpProtocolVersion)
        {
            return false;
        }

        string loadedGameId = string.IsNullOrWhiteSpace(gameId) ? "<none>" : gameId;

        isPlayByPostWaitingForExport = false;
        isPlayByPostFetchInProgress = false;
        playByPostLastFetchWasNoTurn = false;

        if (playByPostPollRoutine != null)
        {
            StopCoroutine(playByPostPollRoutine);
            playByPostPollRoutine = null;
        }

        gameOver = true;
        ResetPbpEndgameRuntimeState();
        SetGameOverUiTitle(PbpVersionMismatchTitle);
        ShowGameOverPopup(PbpVersionMismatchInGameMessage, writeLog: false);
        SetGameOverPrimaryButtonState(
            PbpVersionMismatchExitButtonLabel,
            true,
            PbpEndgamePrimaryAction.BackToMultiplayer);
        RefreshEndTurnButtonInteractable(force: true);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning(
            $"PBp in-game version mismatch popup shown (source={debugSource}, gameId={loadedGameId}, loadedProtocol={loadedProtocolVersion}, supported={SupportedPbpProtocolVersion}).");
#endif

        return true;
    }

    private bool TryShowPbpAppVersionMismatchInGamePopup(string loadedAppVersion, string gameId, string debugSource)
    {
        string loadedGameId = string.IsNullOrWhiteSpace(gameId) ? "<none>" : gameId;
        string loadedVersion = string.IsNullOrWhiteSpace(loadedAppVersion) ? "<missing>" : loadedAppVersion;

        isPlayByPostWaitingForExport = false;
        isPlayByPostFetchInProgress = false;
        playByPostLastFetchWasNoTurn = false;

        if (playByPostPollRoutine != null)
        {
            StopCoroutine(playByPostPollRoutine);
            playByPostPollRoutine = null;
        }

        gameOver = true;
        ResetPbpEndgameRuntimeState();
        SetGameOverUiTitle(PbpVersionMismatchTitle);
        ShowGameOverPopup(PbpAppVersionMismatchInGameMessage, writeLog: false);
        SetGameOverPrimaryButtonState(
            PbpVersionMismatchExitButtonLabel,
            true,
            PbpEndgamePrimaryAction.BackToMultiplayer);
        RefreshEndTurnButtonInteractable(force: true);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning(
            $"PBp in-game app version mismatch popup shown (source={debugSource}, gameId={loadedGameId}, loadedAppVersion={loadedVersion}, currentAppVersion={CurrentAppVersion}).");
#endif

        return true;
    }

    private bool TryComputePbpLocalResult(int winnerSeatIndex, out bool didLocalWin, out int localSeatIndex)
    {
        didLocalWin = false;
        localSeatIndex = 0;

        if (currentMode != GameMode.PlayByPost)
            return false;

        if (!TryGetLocalSeatIndexForPbp(currentGameId, out localSeatIndex))
            return false;

        didLocalWin = winnerSeatIndex == localSeatIndex;
        return true;
    }

    private static bool TryResolvePbpWinnerSeatFromEligibleSeats(
        List<PlayByPostSeatMetadata> normalizedSeats,
        int seatCount,
        out int winnerSeatIndex)
    {
        winnerSeatIndex = -1;
        int normalizedSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(seatCount);
        for (int seatIndex = 0; seatIndex < normalizedSeatCount; seatIndex++)
        {
            PlayByPostSeatMetadata seatMetadata =
                normalizedSeats != null && seatIndex < normalizedSeats.Count
                    ? normalizedSeats[seatIndex]
                    : null;
            if (!IsEligiblePlayByPostSeatForTurnProgression(seatMetadata))
                continue;

            if (winnerSeatIndex >= 0)
                return false;

            winnerSeatIndex = seatIndex;
        }

        return winnerSeatIndex >= 0;
    }

    private void SetPbpWinnerSeatIndex(int winnerSeatIndex)
    {
        if (currentMode != GameMode.PlayByPost)
        {
            pbpWinnerSeatIndex = -1;
            return;
        }

        pbpWinnerSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(
            winnerSeatIndex,
            GetConfiguredPlayByPostSeatCount());
    }

    private bool TryGetPbpWinnerSeatIndex(out int winnerSeatIndex)
    {
        winnerSeatIndex = -1;
        if (currentMode != GameMode.PlayByPost || pbpWinnerSeatIndex < 0)
            return false;

        winnerSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(
            pbpWinnerSeatIndex,
            GetConfiguredPlayByPostSeatCount());
        return true;
    }

    private bool TryConfigurePbpEndgameForWinnerSeat(int winnerSeatIndex, string debugSource)
    {
        SetPbpWinnerSeatIndex(winnerSeatIndex);
        if (!TryComputePbpLocalResult(winnerSeatIndex, out bool didLocalWin, out _))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError(
                $"PBp gameOver result resolution failed (source={debugSource}, gameId={currentGameId ?? "<none>"}, winnerSeatIndex={winnerSeatIndex}).");
#endif
            return false;
        }

        ShowGameOverPopup(didLocalWin ? "You won." : "You lost.", writeLog: false);

        pbpEndgameLocalWinner = didLocalWin;
        pbpEndgameSubmitPending = false;
        pbpEndgameSubmitSucceeded = false;
        pbpEndgameSubmitPayloadCached = false;
        pbpEndgameCachedExportJson = null;
        pbpEndgameCachedExportGameId = null;
        pbpEndgameCachedTransportSeq = 0;
        pbpEndgameCachedExportTurnNumber = 0;
        pbpEndgameCachedExportIsPlayerTurn = false;

        if (didLocalWin)
        {
            SetGameOverPrimaryButtonState("Submitting...", false, PbpEndgamePrimaryAction.Submitting);
            TryStartPbpEndgameAutoSubmit();
            return true;
        }

        SetGameOverPrimaryButtonState("Archive game", true, PbpEndgamePrimaryAction.BackAndArchive);
        return true;
    }

    private void ConfigurePbpEndgameFallback(string debugSource)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogError(
            $"PBp gameOver fell back to neutral result (source={debugSource}, gameId={currentGameId ?? "<none>"}).");
#endif
        ShowGameOverPopup("Game over.", writeLog: false);
        pbpEndgameLocalWinner = false;
        pbpEndgameSubmitPending = false;
        pbpEndgameSubmitSucceeded = false;
        pbpEndgameSubmitPayloadCached = false;
        pbpEndgameCachedExportJson = null;
        pbpEndgameCachedExportGameId = null;
        pbpEndgameCachedTransportSeq = 0;
        pbpEndgameCachedExportTurnNumber = 0;
        pbpEndgameCachedExportIsPlayerTurn = false;
        SetGameOverPrimaryButtonState("Archive game", true, PbpEndgamePrimaryAction.BackAndArchive);
    }

    private void ConfigurePbpEndgameFromLoadedState()
    {
        if (!TryGetPbpWinnerSeatIndex(out int winnerSeatIndex))
        {
            ConfigurePbpEndgameFallback("load_missing_winner");
            return;
        }

        if (!TryConfigurePbpEndgameForWinnerSeat(winnerSeatIndex, "load"))
        {
            ConfigurePbpEndgameFallback("load_result_resolution_failed");
        }
    }

    private bool TryBuildPbpEndgameSubmitPayload()
    {
        if (pbpEndgameSubmitPayloadCached && !string.IsNullOrWhiteSpace(pbpEndgameCachedExportJson))
            return true;

        if (!TryBuildPlayByPostExportSave(out GameSave exportSave, out string exportJson) ||
            exportSave == null ||
            string.IsNullOrWhiteSpace(exportJson))
        {
            return false;
        }

        pbpEndgameCachedExportGameId = exportSave.gameId;
        pbpEndgameCachedExportTurnNumber = exportSave.turnNumber;
        pbpEndgameCachedExportIsPlayerTurn = exportSave.isPlayerTurn;
        pbpEndgameCachedTransportSeq = ComputeTransportSeq(exportSave);
        pbpEndgameCachedExportJson = exportJson;
        pbpEndgameSubmitPayloadCached = true;
        return true;
    }

    private void TryStartPbpEndgameAutoSubmit()
    {
        if (currentMode != GameMode.PlayByPost || !gameOver || !pbpEndgameLocalWinner || pbpEndgameSubmitPending)
            return;

        if (!TryBuildPbpEndgameSubmitPayload())
        {
            pbpEndgameSubmitPending = false;
            pbpEndgameSubmitSucceeded = false;
            SetGameOverPrimaryButtonState("Retry submit", true, PbpEndgamePrimaryAction.RetrySubmit);
            return;
        }

        ResolveTurnTransport();
        pbpEndgameSubmitPending = true;
        pbpEndgameSubmitSucceeded = false;
        SetGameOverPrimaryButtonState("Submitting...", false, PbpEndgamePrimaryAction.Submitting);
        lastAppliedTurnNumberForPolling = pbpEndgameCachedTransportSeq;

        StartCoroutine(SubmitPlayByPostTurnThenStartPolling(
            pbpEndgameCachedTransportSeq,
            pbpEndgameCachedExportJson,
            pbpEndgameCachedExportTurnNumber,
            pbpEndgameCachedExportIsPlayerTurn,
            pbpEndgameCachedExportGameId));
    }

    private void OnPlayByPostSubmitResultForEndgame(bool ok, string err)
    {
        if (!pbpEndgameSubmitPending || currentMode != GameMode.PlayByPost || !gameOver || !pbpEndgameLocalWinner)
            return;

        pbpEndgameSubmitPending = false;
        bool treatAsSuccess = ok || err == TurnTelemetryConstants.Conflict;
        pbpEndgameSubmitSucceeded = treatAsSuccess;
        if (treatAsSuccess)
        {
            SetGameOverPrimaryButtonState("Archive game", true, PbpEndgamePrimaryAction.BackAndArchive);
            return;
        }

        SetGameOverPrimaryButtonState("Retry submit", true, PbpEndgamePrimaryAction.RetrySubmit);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning($"PBp endgame submit failed (err={(err ?? "<null>")}).");
#endif
    }

    private void ReturnToMultiplayerAndArchiveLocalPbpCopy()
    {
        string gameId = GetPbpGameIdFromPrefsOrCurrent();
        ArchiveLocalPbpGameCopy(gameId);

        ReturnToMultiplayer();
    }

    private void ReturnToMultiplayer()
    {
        PlayerPrefs.SetInt(ReturnToMultiplayerPaneKey, 1);
        PlayerPrefs.Save();

        Time.timeScale = 1f;
        SceneManager.LoadScene(MainMenuSceneName);
    }

    private void ArchiveLocalPbpGameCopy(string gameId)
    {
        MainMenuController.ArchiveLocalPlayByPostGame(gameId, clearActiveGameSelection: true, markFinishedLocally: true);
    }

    void EnsureEventSystemExists()
    {
        if (EventSystem.current != null)
            return;

        Debug.LogWarning("TurnManager: No EventSystem detected. Please add one to the gameplay scene.");
    }

    void EndCurrentTurn(bool userInitiated = false)
    {
        if (!CanAdvanceTurn())
            return;

        if (userInitiated)
        {
            TryEmitEndTurnTelemetry();
        }

        if (autoEndTurnRoutine != null)
        {
            StopCoroutine(autoEndTurnRoutine);
            autoEndTurnRoutine = null;
        }

        if (SoundManager.Instance != null && !ShouldSuppressAIVsAIAudio())
        {
            SoundManager.Instance.PlayTurnEnd();
        }

        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.HideAllMoveOutlines();
        }

        if (currentMode == GameMode.VsAI)
        {
            isPlayerTurn = false;
            AutoSaveIfEnabled();
            StartCoroutine(AITurn());
            return;
        }

        // Play-by-Post: both sides are human-controlled with seat ownership.
        if (currentMode == GameMode.PlayByPost)
        {
            // In Play-by-Post we do NOT start the next side's turn locally,
            // otherwise the local player would see the opponent's fog-of-war.
            // Instead we freeze interaction, optionally show a popup, and
            // let CopyCurrentStateToClipboard() build a snapshot that
            // represents the *next* side's turn.
            isPlayByPostWaitingForExport = true;
            if (UnitSelectionManager.Instance != null)
            {
                UnitSelectionManager.Instance.ClearSelection();
            }
            if (CityUIManager.Instance != null)
            {
                CityUIManager.Instance.ClosePanel();
            }
            RefreshEndTurnButtonInteractable(force: true);
            AutoSaveIfEnabled();

            GameSave exportSave = null;
            string exportJson = null;
            if (TryBuildPlayByPostExportSave(out exportSave, out exportJson))
            {
                // Persist the exact post-turn/export snapshot so resume cannot regress
                // to a pre-handoff local-turn state.
                SavePlayByPostPerGameSnapshot(
                    snapshotJson: exportJson,
                    snapshotRoundTurn: exportSave.turnNumber,
                    snapshotIsPlayerTurn: exportSave.isPlayerTurn,
                    snapshotGameId: exportSave.gameId,
                    historySource: "export_write");
            }

            if (playByPostAutoSyncEnabled)
            {
                ResolveTurnTransport();
                if (exportSave != null && !string.IsNullOrWhiteSpace(exportJson))
                {
                    int transportSeq = ComputeTransportSeq(exportSave);
                    if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
                    {
                        Debug.Log(
                            $"PBp export verify: roundTurn={exportSave.turnNumber}, isPlayerTurn={exportSave.isPlayerTurn}, " +
                            $"transportSeq={transportSeq}, lastAppliedTransportSeq={lastAppliedTurnNumberForPolling}");
                    }
                    lastAppliedTurnNumberForPolling = transportSeq;

                    StartCoroutine(SubmitPlayByPostTurnThenStartPolling(
                        transportSeq,
                        exportJson,
                        exportSave.turnNumber,
                        exportSave.isPlayerTurn,
                        exportSave.gameId));
                    RefreshEndTurnButtonInteractable(force: true);
                }
            }
        }
    }

    private void ResolveTurnTransport()
    {
        float start = Time.realtimeSinceStartup;
        ITurnTransport resolved = null;
        bool resolvedWasNull = false;
        string telemetryTransportName = null;

        if (turnTransportComponent != null && turnTransportComponent is ITurnTransport componentTransport)
        {
            resolved = componentTransport;
            telemetryTransportName = resolved.TransportName;
            resolved.Initialize();
        }
        else
        {
            if (transportProvider == null)
            {
                transportProvider = GetComponent<TurnTransportProvider>();
                if (transportProvider == null)
                {
                    transportProvider = gameObject.AddComponent<TurnTransportProvider>();
                    transportProvider.kind = TurnTransportProvider.TransportKind.InMemory;
                }
            }

            resolved = transportProvider != null ? transportProvider.GetTransport() : null;
            if (resolved == null)
            {
                resolvedWasNull = true;
                telemetryTransportName = TurnTelemetryConstants.ProviderNullTransport;
                resolved = new NullTurnTransport();
                resolved.Initialize();
            }
        }

        if (resolved != null && telemetryTransportName == null)
        {
            telemetryTransportName = resolved.TransportName;
        }

        bool ok = resolved != null && resolved.IsAvailable;
        string err = ok ? null : (resolvedWasNull ? TurnTelemetryConstants.NullTransport : TurnTelemetryConstants.Unavailable);

        if (string.IsNullOrWhiteSpace(telemetryTransportName))
        {
            telemetryTransportName = resolved != null ? resolved.TransportName : "Null";
        }

        turnTransport = resolved;
        if (!(turnTransport is TelemetryTurnTransport))
        {
            turnTransport = new TelemetryTurnTransport(
                turnTransport,
                telemetrySink,
                () => currentMode.ToString(),
                GetCurrentGameIdHash);
        }

        float durationMs = (Time.realtimeSinceStartup - start) * 1000f;
        TryEmitTransportTelemetry(
            TurnTelemetryConstants.Resolve,
            telemetryTransportName,
            ok,
            err,
            durationMs,
            0,
            null,
            null);
    }

    private void ResolveTelemetrySink()
    {
        if (telemetrySinkComponent != null && telemetrySinkComponent is ITurnTelemetrySink sink)
        {
            telemetrySink = sink;
        }
        else
        {
            telemetrySink = NullTurnTelemetrySink.Instance;
        }
    }

    private void TryEmitTransportTelemetry(
        string op,
        string transport,
        bool ok,
        string err,
        float durationMs,
        int payloadChars,
        int? seqA,
        int? seqB)
    {
        if (telemetrySink == null)
            return;

        try
        {
            telemetrySink.OnTransportOp(
                op,
                transport,
                ok,
                err,
                durationMs,
                payloadChars,
                seqA,
                seqB,
                currentMode.ToString(),
                GetCurrentGameIdHash());
        }
        catch
        {
        }
    }

    private void TryEmitEndTurnTelemetry()
    {
        if (telemetrySink == null)
            return;

        try
        {
            telemetrySink.OnEndTurnPressed(
                currentMode.ToString(),
                turnNumber,
                GetCurrentGameIdHash());
        }
        catch
        {
        }
    }

    private void SetCurrentGameId(string gameId)
    {
        if (currentGameId == gameId)
            return;

        currentGameId = gameId;
        cachedGameIdRaw = null;
        cachedGameIdHash = null;
    }

    private string GetCurrentGameIdHash()
    {
        if (string.IsNullOrEmpty(currentGameId))
            return null;

        if (currentGameId != cachedGameIdRaw)
        {
            cachedGameIdRaw = currentGameId;
            cachedGameIdHash = Hash128.Compute(currentGameId).ToString();
        }

        return cachedGameIdHash;
    }

    private string GetPlayByPostStateSummary()
    {
        string gameIdForLog = string.IsNullOrWhiteSpace(currentGameId) ? "<none>" : currentGameId;
        int transportSeq = ComputeTransportSeq(turnNumber, GetAuthoritativeCurrentTurnSeatIndex(), GetRuntimeSeatCount());
        return $"mode={currentMode},gameId={gameIdForLog},roundTurn={turnNumber},isPlayerTurn={isPlayerTurn},currentTurnSeatIndex={GetAuthoritativeCurrentTurnSeatIndex()},isWaitingForExport={isPlayByPostWaitingForExport},transportSeq={transportSeq},lastAppliedTransportSeq={lastAppliedTurnNumberForPolling}";
    }

    private string GetPlayByPostLoadRelationToLastSubmit()
    {
#if DEVELOPMENT_BUILD
        if (lastSubmittedTransportSeqForTelemetry < 0)
            return "no-submit-recorded";

        if (!string.Equals(lastSubmittedGameIdForTelemetry, currentGameId, System.StringComparison.Ordinal))
            return "different-game";

        if (lastAppliedTurnNumberForPolling < lastSubmittedTransportSeqForTelemetry)
            return "pre-submit";

        if (lastAppliedTurnNumberForPolling == lastSubmittedTransportSeqForTelemetry)
            return "matches-submit";

        return "post-submit";
#else
        return "n/a";
#endif
    }

    private void LogPlayByPostTelemetry(string eventName, string details)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!PbpDebugSettingsLoader.EnableSaveLoadLogs)
            return;

        Debug.Log($"[PBpTelemetry] {eventName} {details}");
#endif
    }

    private IEnumerator SubmitPlayByPostTurnThenStartPolling(
        int transportSeq,
        string exportJson,
        int exportTurnNumber,
        bool exportIsPlayerTurn,
        string exportGameId)
    {
        float submitStartedAt = Time.realtimeSinceStartup;
        LogPlayByPostTelemetry(
            "SubmitStart",
            $"gameId={currentGameId} exportTurn={exportTurnNumber} exportIsPlayerTurn={exportIsPlayerTurn} transportSeq={transportSeq} isPlayByPostWaitingForExport={isPlayByPostWaitingForExport} currentMode={currentMode} state={GetPlayByPostStateSummary()}");
#if DEVELOPMENT_BUILD
        lastSubmittedTransportSeqForTelemetry = transportSeq;
        lastSubmittedGameIdForTelemetry = currentGameId;
#endif

        if (turnTransport == null || !turnTransport.IsAvailable)
        {
            float durationMs = (Time.realtimeSinceStartup - submitStartedAt) * 1000f;
            LogPlayByPostTelemetry(
                "SubmitResult",
                $"ok=false err={TurnTelemetryConstants.Unavailable} durationMs={durationMs:F1} transportSeq={transportSeq} lastAppliedTransportSeq={lastAppliedTurnNumberForPolling} state={GetPlayByPostStateSummary()}");
            Debug.LogWarning("Play-by-Post auto-sync is enabled, but no transport is available. Manual copy/paste remains available.");
            TryNotifyPlayByPostSubmitResult(false, TurnTelemetryConstants.Unavailable);
            HandlePlayByPostSubmitConnectivityFailure(TurnTelemetryConstants.Unavailable);
            yield break;
        }

        bool submitOk = false;
        string submitError = null;

        yield return turnTransport.SubmitTurn(currentGameId, transportSeq, exportJson, (ok, err) =>
        {
            submitOk = ok;
            submitError = err;
        });

        float submitDurationMs = (Time.realtimeSinceStartup - submitStartedAt) * 1000f;
        LogPlayByPostTelemetry(
            "SubmitResult",
            $"ok={submitOk} err={(submitError ?? "<null>")} durationMs={submitDurationMs:F1} transportSeq={transportSeq} lastAppliedTransportSeq={lastAppliedTurnNumberForPolling} state={GetPlayByPostStateSummary()}");

        if (submitOk)
        {
            if (IsCurrentPlayByPostFirstRemoteSubmitPendingForUi())
            {
                pbpCreatorFirstRemoteSubmitPending = false;
            }

            Debug.Log($"PBp submit ok via {turnTransport.TransportName} (gameId={currentGameId}, turn={transportSeq}).");
        }
        else
        {
            Debug.LogWarning($"PBp submit failed via {turnTransport.TransportName} (gameId={currentGameId}, turn={transportSeq}): {submitError}");
        }

        TryNotifyPlayByPostSubmitResult(submitOk, submitError);
        bool isWinningPbpEndgameSubmit =
            currentMode == GameMode.PlayByPost &&
            gameOver &&
            pbpEndgameLocalWinner;
        bool didWinningPbpEndgameSubmitSucceed =
            submitOk ||
            submitError == TurnTelemetryConstants.Conflict;
        if (isWinningPbpEndgameSubmit && !didWinningPbpEndgameSubmitSucceed)
        {
            yield break;
        }

        if (submitOk)
        {
            int exportSeatCount = GetConfiguredPlayByPostSeatCount();
            int exportCurrentTurnSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(
                transportSeq % Mathf.Max(1, exportSeatCount),
                exportSeatCount);

            // Re-save on successful submit so disk state always matches the submitted turn.
            SavePlayByPostPerGameSnapshot(
                snapshotJson: exportJson,
                snapshotRoundTurn: exportTurnNumber,
                snapshotIsPlayerTurn: exportIsPlayerTurn,
                snapshotGameId: exportGameId,
                historySource: "submit_write");
            SaveManifestService.RecordPlayByPostExport(
                currentGameId,
                turnTransport != null ? turnTransport.TransportName : null,
                lastKnownRoundTurn: exportTurnNumber,
                lastKnownIsPlayerTurn: exportIsPlayerTurn,
                lastKnownCurrentTurnSeatIndex: exportCurrentTurnSeatIndex,
                lastKnownTransportSeq: transportSeq,
                lastKnownSeatCount: exportSeatCount);
            StartPlayByPostPolling(transportSeq);
            yield break;
        }

        if (IsConnectivityLikeTransportError(submitError))
        {
            HandlePlayByPostSubmitConnectivityFailure(submitError);
            yield break;
        }

        StartPlayByPostPolling(transportSeq);
    }

    private IEnumerator SubmitPlayByPostResignationAndArchive(
        string resignedJson,
        int exportTurnNumber,
        bool exportIsPlayerTurn,
        int exportTransportSeq,
        int exportSeatCount,
        bool exportGameOver)
    {
        if (string.IsNullOrWhiteSpace(resignedJson) ||
            string.IsNullOrWhiteSpace(currentGameId))
        {
            playByPostResignationSubmitInFlight = false;
            yield break;
        }

        if (turnTransport == null || !turnTransport.IsAvailable)
        {
            playByPostResignationSubmitInFlight = false;
            yield break;
        }

        bool submitOk = false;
        string submitError = null;
        yield return turnTransport.SubmitTurn(currentGameId, exportTransportSeq, resignedJson, (ok, err) =>
        {
            submitOk = ok;
            submitError = err;
        });

        playByPostResignationSubmitInFlight = false;

        if (!submitOk)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"PBp resign submit failed (gameId={currentGameId}, seq={exportTransportSeq}, err={submitError ?? "<null>"}).");
#endif
            yield break;
        }

        SavePlayByPostPerGameSnapshot(
            snapshotJson: resignedJson,
            snapshotRoundTurn: exportTurnNumber,
            snapshotIsPlayerTurn: exportIsPlayerTurn,
            snapshotGameId: currentGameId,
            historySource: "resign_submit_write");
        SaveManifestService.RecordPlayByPostExport(
            currentGameId,
            turnTransport != null ? turnTransport.TransportName : null,
            lastKnownRoundTurn: exportTurnNumber,
            lastKnownIsPlayerTurn: exportIsPlayerTurn,
            lastKnownCurrentTurnSeatIndex: exportTransportSeq % Mathf.Max(1, exportSeatCount),
            lastKnownTransportSeq: exportTransportSeq,
            lastKnownSeatCount: exportSeatCount);

        if (exportGameOver)
        {
            gameOver = true;

            GameSave resignedSave = null;
            try
            {
                resignedSave = JsonUtility.FromJson<GameSave>(resignedJson);
            }
            catch
            {
                resignedSave = null;
            }

            if (resignedSave != null && resignedSave.hasWinnerSeatIndex)
            {
                if (!TryConfigurePbpEndgameForWinnerSeat(resignedSave.winnerSeatIndex, "resign"))
                {
                    ConfigurePbpEndgameFallback("resign_result_resolution_failed");
                }
            }
            else
            {
                ConfigurePbpEndgameFallback("resign_missing_winner");
            }

            RefreshEndTurnButtonInteractable(force: true);
            yield break;
        }

        MainMenuController.ArchiveLocalPlayByPostGame(
            currentGameId,
            clearActiveGameSelection: true,
            markFinishedLocally: exportGameOver);
        ReturnToMultiplayer();
    }

    private void TryNotifyPlayByPostSubmitResult(bool ok, string err)
    {
        if (PlayByPostSubmitResult == null)
            return;

        try
        {
            PlayByPostSubmitResult.Invoke(ok, err);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void TryNotifyPlayByPostFetchResult(bool reachable, string resultOrError)
    {
        if (PlayByPostFetchResult == null)
            return;

        try
        {
            PlayByPostFetchResult.Invoke(reachable, resultOrError);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void HandlePlayByPostSubmitConnectivityFailure(string submitError)
    {
        isPlayByPostWaitingForExport = false;
        isPlayByPostFetchInProgress = false;
        playByPostLastFetchWasNoTurn = false;

        if (playByPostPollRoutine != null)
        {
            StopCoroutine(playByPostPollRoutine);
            playByPostPollRoutine = null;
        }

        SavePlayByPostPerGameSnapshot(historySource: "connectivity_rewrite");
        RefreshEndTurnButtonInteractable(force: true);

        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.RefreshMoveOutlinesForCurrentTurn();
        }

        Debug.LogWarning($"PBp submit connectivity failure. Keeping local turn active (err={(submitError ?? "<null>")}).");
    }

    private void StartPlayByPostPolling(int afterTurnNumber)
    {
        if (playByPostPollRoutine != null)
        {
            StopCoroutine(playByPostPollRoutine);
            playByPostPollRoutine = null;
        }

        lastAppliedTurnNumberForPolling = afterTurnNumber;
        playByPostPollRoutine = StartCoroutine(PlayByPostPollLoop());
    }

    private IEnumerator PlayByPostPollLoop()
    {
        float pollSeconds = Mathf.Max(0.25f, playByPostPollSeconds);

        while (isPlayByPostWaitingForExport)
        {
            yield return TryFetchPlayByPostTurnOnce();
            if (!isPlayByPostWaitingForExport)
                break;

            yield return new WaitForSecondsRealtime(pollSeconds);
        }

        playByPostPollRoutine = null;
    }

    private IEnumerator TryFetchPlayByPostTurnOnce()
    {
        float now = Time.realtimeSinceStartup;
#if DEVELOPMENT_BUILD
        if (!playByPostLastFetchWasNoTurn || (now - playByPostLastNoTurnLogTime) >= PlayByPostNoTurnLogCooldownSeconds)
        {
            if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
            {
                Debug.Log($"PBp fetch attempt started (gameId={currentGameId}, expectedTurn={lastAppliedTurnNumberForPolling + 1})");
            }
        }
#endif
        if (!isPlayByPostWaitingForExport || currentMode != GameMode.PlayByPost)
            yield break;

        if (isPlayByPostFetchInProgress)
            yield break;

        if (turnTransport == null || !turnTransport.IsAvailable)
        {
            TryNotifyPlayByPostFetchResult(false, TurnTelemetryConstants.Unavailable);
            yield break;
        }

        isPlayByPostFetchInProgress = true;
        bool ok = false;
        string err = null;
        int fetchedTurnNumber = 0;
        string json = null;
        int afterTurnNumber = lastAppliedTurnNumberForPolling;

        yield return turnTransport.TryFetchNextTurn(currentGameId, afterTurnNumber, (success, error, turn, fetchedJson) =>
        {
            ok = success;
            err = error;
            fetchedTurnNumber = turn;
            json = fetchedJson;
        });

        string fetchResultOrError = ok
            ? "OK"
            : (string.IsNullOrEmpty(err) ? TurnTelemetryConstants.Unknown : err);
        bool fetchReachable = ok || !IsConnectivityLikeTransportError(fetchResultOrError);
        TryNotifyPlayByPostFetchResult(fetchReachable, fetchResultOrError);

        bool isNoTurn = !ok && err == TurnTelemetryConstants.NoTurn;
        bool shouldLogNoTurn = isNoTurn &&
                               (!playByPostLastFetchWasNoTurn || (now - playByPostLastNoTurnLogTime) >= PlayByPostNoTurnLogCooldownSeconds);

#if DEVELOPMENT_BUILD
        if (!isNoTurn || shouldLogNoTurn)
        {
            if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
            {
                Debug.Log($"PBp fetch result via {turnTransport.TransportName} (ok={ok}, turn={(fetchedTurnNumber != 0 ? fetchedTurnNumber.ToString() : "<none>")}, jsonLen={(json != null ? json.Length : 0)}, err={(err ?? "<null>")})");
            }
        }
#endif

        if (isNoTurn)
        {
            playByPostLastNoTurnLogTime = now;
        }

        playByPostLastFetchWasNoTurn = isNoTurn;
        isPlayByPostFetchInProgress = false;

        if (!ok)
        {
            if (!string.IsNullOrEmpty(err) && err != TurnTelemetryConstants.NoTurn)
            {
                Debug.LogWarning($"PBp fetch failed via {turnTransport.TransportName} (gameId={currentGameId}, after={afterTurnNumber}): {err}");
            }
            yield break;
        }

        if (fetchedTurnNumber <= afterTurnNumber)
        {
            yield break;
        }

        if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
        {
            Debug.Log($"PBp fetch verify: fetchedTransportSeq={fetchedTurnNumber}, previousTransportSeq={lastAppliedTurnNumberForPolling}");
            Debug.Log($"PBp fetched turn {fetchedTurnNumber} via {turnTransport.TransportName} ({(json != null ? json.Length : 0)} chars).");
        }

        isApplyingFetchedPlayByPostSnapshot = true;
        bool loaded = false;
        try
        {
            loaded = LoadFromJsonString(json);
        }
        finally
        {
            isApplyingFetchedPlayByPostSnapshot = false;
        }
        if (loaded)
        {
            lastAppliedTurnNumberForPolling = fetchedTurnNumber;
            if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
            {
                Debug.Log($"PBp loaded turn {fetchedTurnNumber} successfully.");
            }
        }
        else
        {
            Debug.LogWarning($"PBp fetched turn {fetchedTurnNumber}, but failed to load JSON.");
        }
    }

    private static bool IsConnectivityLikeTransportError(string err)
    {
        if (string.IsNullOrEmpty(err))
            return true;

        return err == TurnTelemetryConstants.IoError ||
               err == TurnTelemetryConstants.Unavailable ||
               err == TurnTelemetryConstants.NullTransport ||
               err == TurnTelemetryConstants.Unknown;
    }

    void TryStartGameplayMusic()
    {
        if (!playMusicOnStart)
            return;

        if (SoundManager.Instance == null)
            return;

        // If we already have a playlist running from the menu scene,
        // don't override it unless a specific gameplay track is provided.
        if (gameplayMusic == null && SoundManager.Instance.HasPlaylistConfigured())
            return;

        // Avoid restarting music unnecessarily when it is already playing.
        if (gameplayMusic == null && SoundManager.Instance.IsMusicPlaying())
            return;

        SoundManager.Instance.PlayBackgroundMusic(gameplayMusic);
    }

    IEnumerator AITurn()
    {
        if (gameOver || currentMode != GameMode.VsAI)
            yield break;

        if (SoundManager.Instance != null && !ShouldSuppressAIVsAIAudio())
        {
            SoundManager.Instance.PlayTurnStart();
        }

        // Simulate thinking time
        yield return new WaitForSeconds(aiTurnDelay);

        // Collect AI income at the start of its turn.
        CollectAIGold();

        // AI actions: recruit and move units.
        ResetRecruitmentForAICities();
        if (!disableAI)
        {
            TryCaptureAIDecisionSnapshot(false);
            RunAITurnForSide(false);
        }

        if (gameOver)
            yield break;

        // Back to player
        turnNumber++;
        BeginSideTurn(true, playTurnStartSound: true);
    }

    void BeginPlayerTurn()
    {
        BeginSideTurn(true, playTurnStartSound: true);
    }

    System.Collections.IEnumerator StartupSequence()
    {
        if (TryConsumeForceNewPlayByPostRequest(out string forcedNewGameId))
        {
            pbpCreatorBootstrapGameId = forcedNewGameId;
            pbpCreatorFirstRemoteSubmitPending = true;
            SetGameMode(GameMode.PlayByPost);
            SetCurrentGameId(forcedNewGameId);
            if (!LocalPlayerSeatStore.TryGetSeat(forcedNewGameId, out _))
            {
                LocalPlayerSeatStore.SetSeat(forcedNewGameId, 0);
            }
            PersistCurrentPbpGameIdIfNeeded();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[PBpForceNew] starting new pbp gameId={forcedNewGameId}");
#endif
            // Force-new is authoritative; ignore any stale pending explicit load.
            SaveLoadRequest.TryConsume(out _);
            // Ensure the grid is initialized before starting the forced new PBp game.
            yield return WaitForGridReady();
            InitializeNewGame();
            RecalculatePlayerVisibility();
            RefreshEndTurnButtonInteractable(force: true);
            yield break;
        }

        // Ensure the grid is initialized before applying save or starting a new game.
        yield return WaitForGridReady();

        // Attempt to load a pending save request before starting a new game.
        if (SaveLoadRequest.TryConsume(out string loadPath))
        {
            LogPlayByPostTelemetry(
                "ResumeOpenRequest",
                $"loadPath={loadPath} state={GetPlayByPostStateSummary()}");
            bool loaded = LoadFromFile(loadPath);
            if (loaded)
            {
                Debug.Log("Loaded save from " + loadPath + " on scene start.");
                yield break;
            }

            Debug.LogWarning("Load request failed; starting a new game. Path: " + loadPath);
        }

        ResolveStartupModeForResume();

        if (currentMode == GameMode.PlayByPost)
        {
            string pbpGameId = GetPbpGameIdFromPrefsOrCurrent();
            if (!string.IsNullOrWhiteSpace(pbpGameId))
            {
                string pbpSnapshotPath = GetPbpPerGameSavePath(pbpGameId);
                if (File.Exists(pbpSnapshotPath))
                {
                    LogPlayByPostTelemetry(
                        "ResumeLocalSnapshotAttempt",
                        $"gameId={pbpGameId} path={pbpSnapshotPath} state={GetPlayByPostStateSummary()}");

                    if (LoadFromFile(pbpSnapshotPath))
                    {
                        RefreshEndTurnButtonInteractable(force: true);
                        currentMode = GameMode.PlayByPost;
                        SetCurrentGameId(pbpGameId);
                        PersistCurrentPbpGameIdIfNeeded();
                        RecalculatePlayerVisibility();

                        LogPlayByPostTelemetry(
                            "ResumeLocalSnapshotLoaded",
                            $"gameId={pbpGameId} path={pbpSnapshotPath} state={GetPlayByPostStateSummary()}");
                        Debug.Log($"Loaded PBp local snapshot from {pbpSnapshotPath} on scene start.");
                        yield break;
                    }

                    Debug.LogWarning($"Failed to load PBp local snapshot from {pbpSnapshotPath}; falling back to normal startup.");
                }
            }
        }

        InitializeNewGame();
    }

    private static bool TryConsumeForceNewPlayByPostRequest(out string gameId)
    {
        gameId = null;
        if (PlayerPrefs.GetInt(PlayByPostForceNewKey, 0) != 1)
        {
            return false;
        }

        gameId = PlayerPrefs.GetString(PlayByPostPendingNewGameIdKey, string.Empty);
        PlayerPrefs.DeleteKey(PlayByPostForceNewKey);
        PlayerPrefs.DeleteKey(PlayByPostPendingNewGameIdKey);
        PlayerPrefs.Save();

        if (string.IsNullOrWhiteSpace(gameId))
        {
            gameId = System.Guid.NewGuid().ToString();
        }

        return true;
    }

    private void ResolveStartupModeForResume()
    {
        if (currentMode != GameMode.None)
            return;

        if (GameModeSelection.TryConsume(out GameMode pendingMode))
        {
            SetGameMode(pendingMode);
            return;
        }

        if (HasPlayByPostSessionContext())
        {
            SetGameMode(GameMode.PlayByPost);
            Debug.LogWarning("No mode preselected, but PBp context detected. Forcing PlayByPost mode.");
        }
    }

    System.Collections.IEnumerator WaitForGridReady()
    {
        while (gridManager == null || gridManager.tileGrid == null || gridManager.tileGrid.Length == 0)
        {
            yield return null;
        }
    }

    void InitializeNewGame()
    {
        // Reset core state for a fresh game (important when reloading scenes in-editor).
        ResetPlayByPostRuntimeState();
        gameOver = false;
        ResetGameOverUiState();
        turnNumber = 1;
        currentTurnSeatIndex = 0;
        SyncLegacyTurnOwnerBridge();
        playerGold = startingGold;
        aiGold = startingGold;
        InitializeSeatGoldForNewGame(PlayByPostSeatUtility.MinSeatCount);
        aiRecruitVariant = AIRecruitVariant.Default;

        if (GameModeSelection.TryConsume(out GameMode pendingMode))
        {
            SetGameMode(pendingMode);
        }
        else if (currentMode == GameMode.None && HasPlayByPostSessionContext())
        {
            // Defensive fallback: if menu-to-game pending mode was lost but
            // PBp session context exists, keep this run in PlayByPost.
            SetGameMode(GameMode.PlayByPost);
            Debug.LogWarning("No mode preselected, but PBp context detected. Forcing PlayByPost mode.");
        }
        else if (currentMode == GameMode.None)
        {
            SetGameMode(GameMode.VsAI);
        }

        MapSizePreset selectedMapSize = GetDefaultMapSizePreset();
        if (MapSizeSelection.TryConsume(out MapSizePreset pendingMapSize))
        {
            selectedMapSize = pendingMapSize;
        }

        configuredPlayByPostSeatCount = PlayByPostSeatUtility.MinSeatCount;
        if (currentMode == GameMode.PlayByPost &&
            PlayByPostSeatCountSelection.TryConsume(out int pendingPlayByPostSeatCount))
        {
            configuredPlayByPostSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(pendingPlayByPostSeatCount);
        }

        InitializeSeatGoldForNewGame(currentMode == GameMode.PlayByPost
            ? configuredPlayByPostSeatCount
            : PlayByPostSeatUtility.MinSeatCount);
        SetCurrentTurnSeatIndexForRuntime(0, currentMode == GameMode.PlayByPost
            ? configuredPlayByPostSeatCount
            : PlayByPostSeatUtility.MinSeatCount);

        GetBoardDimensionsForPreset(selectedMapSize, out int boardWidth, out int boardHeight);
        EnsureBoardDimensions(boardWidth, boardHeight);

        if (currentMode == GameMode.PlayByPost)
        {
            InitializePlayByPostSession();
        }
        else if (string.IsNullOrEmpty(currentGameId))
        {
            SetCurrentGameId(System.Guid.NewGuid().ToString());
        }

        if (AIRecruitVariantSelection.TryConsume(out AIRecruitVariant pendingRecruitVariant))
        {
            aiRecruitVariant = pendingRecruitVariant;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        ResolveAIVsAIDebugRuntimeSettings(consumePendingSelection: true);
        ResolveAIVsAIBatchRuntimeSettings(consumePendingSelection: true);
        if (ShouldSuppressAIVsAIAudio() && SoundManager.Instance != null)
        {
            SoundManager.Instance.StopMusic();
        }
#endif

        if (SnapshotHistorySelection.TryConsume(out bool pendingStoreSnapshotHistory))
        {
            MatchSnapshotHistorySettings.SetEnabled(currentGameId, pendingStoreSnapshotHistory);
        }

        ResetRecruitmentForPlayerCities();
        // Give income only to the side whose turn it is.
        // The other side receives income at the start of
        // its first turn (via Begin*Turn / AITurn).
        if (currentMode == GameMode.PlayByPost)
        {
            CollectIncomeForSeat(currentTurnSeatIndex);
        }
        else
        {
            CollectPlayerIncome();
        }
        RecalculatePlayerVisibility();

        ScheduleAutoEndTurnCheck();

        RefreshEndTurnButtonInteractable(force: true);

        StartAIVsAIDebugLoopIfNeeded();
    }

    private bool HasPlayByPostSessionContext()
    {
        string pbpGameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(pbpGameId))
            return false;

        string snapshotPath = GetPbpPerGameSavePath(pbpGameId);
        if (!string.IsNullOrWhiteSpace(snapshotPath) && File.Exists(snapshotPath))
            return true;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        try
        {
            // Dev-only probe to surface manifest IO/parsing issues without
            // using manifest state to force PBp mode at runtime.
            SaveManifestService.GetActivePlayByPostGames();
        }
        catch (System.Exception ex)
        {
            if (!hasLoggedManifestProbeFailure)
            {
                Debug.LogWarning(
                    $"PBp session context: manifest probe failed ({ex.GetType().Name}): {ex.Message}");
                hasLoggedManifestProbeFailure = true;
            }
        }
#endif

        return false;
    }

    private void InitializePlayByPostSession()
    {
        string gameId = !string.IsNullOrWhiteSpace(currentGameId)
            ? currentGameId
            : PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        bool createdGameId = false;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            gameId = System.Guid.NewGuid().ToString();
            PlayerPrefs.SetString(PlayByPostGameIdKey, gameId);
            PlayerPrefs.Save();
            createdGameId = true;
        }
        SetCurrentGameId(gameId);
        PersistCurrentPbpGameIdIfNeeded();
        if (createdGameId)
        {
            LocalPlayerSeatStore.SetSeat(gameId, 0);
            pbpCreatorBootstrapGameId = gameId;
            pbpCreatorFirstRemoteSubmitPending = true;
        }

        bool readinessOk = EnsurePlayByPostControlReadiness();
        if (!readinessOk || !LocalIsPlayerOwned())
        {
            isPlayByPostWaitingForExport = true;
            lastAppliedTurnNumberForPolling = -1;
            if (playByPostAutoSyncEnabled && !string.IsNullOrWhiteSpace(currentGameId))
            {
                ResolveTurnTransport();
                StartPlayByPostPolling(-1);
            }
        }
    }

    public void ScheduleAutoEndTurnCheck()
    {
        if (!autoEndTurnWhenNoActions)
            return;

        if (gameOver || !IsHumanTurn())
            return;

        // Vs AI player only.
        if (currentMode != GameMode.VsAI || !isPlayerTurn)
            return;

        if (autoEndTurnRoutine != null)
        {
            StopCoroutine(autoEndTurnRoutine);
            autoEndTurnRoutine = null;
        }

        autoEndTurnRoutine = StartCoroutine(AutoEndTurnAfterDelay());
    }

    IEnumerator AutoEndTurnAfterDelay()
    {
        float start = Time.unscaledTime;
        float earliestCheckTime = start + Mathf.Max(0f, autoEndTurnDelaySeconds);

        while (true)
        {
            if (!autoEndTurnWhenNoActions)
                break;

            if (gameOver || !IsHumanTurn())
                break;

            if (currentMode != GameMode.VsAI || !isPlayerTurn)
                break;

            // If UI panels are visible, wait a bit and check again.
            if (IsAnyActionPanelOpen())
            {
                yield return new WaitForSecondsRealtime(0.25f);
                continue;
            }

            float now = Time.unscaledTime;
            float wait = 0f;

            if (now < earliestCheckTime)
            {
                wait = earliestCheckTime - now;
            }

            float sinceInput = now - lastHumanInputUnscaledTime;
            if (sinceInput < autoEndTurnInputCooldownSeconds)
            {
                float remaining = autoEndTurnInputCooldownSeconds - sinceInput;
                wait = Mathf.Max(wait, remaining);
            }

            if (wait > 0f)
            {
                yield return new WaitForSecondsRealtime(wait);
                continue;
            }

            if (HasAnyAvailableActionForCurrentPlayer())
                break;

#if ENABLE_AUTO_END_TURN_ON_NO_ACTIONS
            if (PbpDebugSettingsLoader.EnableInputLogs)
            {
                Debug.Log("Auto-ending turn: no available actions.");
            }
            EndCurrentTurn(false);
            break;
#else
            if (!autoEndTurnDisabledLoggedThisTurn)
            {
                if (PbpDebugSettingsLoader.EnableInputLogs)
                {
                    Debug.Log("Auto-end turn on no-actions is temporarily disabled.");
                }
                autoEndTurnDisabledLoggedThisTurn = true;
            }
            break;
#endif
        }

        autoEndTurnRoutine = null;
    }

    private bool IsAnyActionPanelOpen()
    {
        // If a player is reading a panel, don't auto-end under them.
        if (CityUIManager.Instance != null &&
            CityUIManager.Instance.IsPanelOpen)
        {
            return true;
        }

        if (UnitUIManager.Instance != null &&
            UnitUIManager.Instance.IsPanelOpen)
        {
            return true;
        }

        return false;
    }

    private bool HasAnyAvailableActionForCurrentPlayer()
    {
        if (currentMode != GameMode.VsAI || !isPlayerTurn)
            return false;

        // 1) Recruitment options
        City[] cities = Object.FindObjectsByType<City>();
        List<UnitDefinition> recruitableUnits = GetRecruitableOfficialUnitDefinitions();
        for (int unitIndex = 0; unitIndex < recruitableUnits.Count; unitIndex++)
        {
            UnitDefinition unitDefinition = recruitableUnits[unitIndex];
            if (unitDefinition == null || playerGold < unitDefinition.RecruitCost)
                continue;

            foreach (City city in cities)
            {
                if (city == null || !city.isPlayerOwned)
                    continue;

                // Avoid City.CanRecruit() here because it logs warnings meant for player clicks.
                if (city.stationedUnit != null || city.hasRecruitedThisTurn)
                    continue;

                // Spawn logic also checks occupancy; match that here.
                if (GridUtils.IsTileOccupied(city.transform.position, null))
                    continue;

                return true;
            }
        }

        // 2) Unit movement / attacks.
        float tileSize = gridManager != null ? Mathf.Max(0.01f, gridManager.tileSize) : 1f;

        Unit[] units = Object.FindObjectsByType<Unit>();
        foreach (Unit unit in units)
        {
            if (unit == null || !unit.isPlayerOwned)
                continue;

            if (HasAnyLegalAction(unit, tileSize))
                return true;
        }

        return false;
    }

    private bool HasAnyLegalAction(Unit unit, float tileSize)
    {
        if (unit == null)
            return false;

        bool canMove = unit.CanMoveThisTurn();
        bool canAttack = unit.CanAttackThisTurn();

        if (!canMove && !canAttack)
            return false;

        Vector3 from = unit.transform.position;
        from.z = 0f;

        if (canAttack)
        {
            int attackRange = Mathf.Max(1, unit.AttackRange);
            for (int dx = -attackRange; dx <= attackRange; dx++)
            {
                for (int dy = -attackRange; dy <= attackRange; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    Vector3 to = new Vector3(from.x + dx * tileSize, from.y + dy * tileSize, 0f);
                    TileVisibility targetTile = null;

                    if (gridManager != null && !gridManager.TryGetTileAtWorldPosition(to, out targetTile))
                        continue;

                    Unit occupant = GridUtils.GetUnitAtPosition(to, unit);
                    if (occupant != null &&
                        occupant.isPlayerOwned != unit.isPlayerOwned &&
                        (targetTile == null || targetTile.isVisibleNow))
                    {
                        return true;
                    }
                }
            }
        }

        if (!canMove)
            return false;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                Vector3 to = new Vector3(from.x + dx * tileSize, from.y + dy * tileSize, 0f);

                // Optional bounds check if we can determine a tile.
                if (gridManager != null && !gridManager.TryGetTileAtWorldPosition(to, out _))
                    continue;

                Unit occupant = GridUtils.GetUnitAtPosition(to, unit);
                if (occupant == null)
                {
                    // Empty tile: can always move there (city capture is just moving onto the tile).
                    return true;
                }
            }
        }

        return false;
    }

    void ResetRecruitmentForPlayerCities()
    {
        ResetRecruitmentForSeat(0);
    }

    void ResetRecruitmentForAICities()
    {
        ResetRecruitmentForSeat(1);
    }

    void RunAI()
    {
        RunAIForSide(false);
    }

    private void RunAITurnForSide(bool actingSideIsPlayerOwned)
    {
        RunAIForSide(actingSideIsPlayerOwned);
    }

    private void ResetRecruitmentForSeat(int ownerSeatIndex)
    {
        City[] cities = Object.FindObjectsByType<City>();
        foreach (City city in cities)
        {
            if (city != null && city.ownerSeatIndex == ownerSeatIndex)
            {
                city.hasRecruitedThisTurn = false;
            }
        }
    }

    private void ResetRecruitmentForSide(bool sideIsPlayerOwned)
    {
        ResetRecruitmentForSeat(sideIsPlayerOwned ? 0 : 1);
    }

    private void CollectIncomeForSeat(int ownerSeatIndex)
    {
        if (gameOver)
            return;

        int baseIncome = 0;
        City[] cities = Object.FindObjectsByType<City>();
        foreach (City city in cities)
        {
            if (city != null && city.ownerSeatIndex == ownerSeatIndex)
            {
                baseIncome += goldPerCity;
            }
        }

        int income = ownerSeatIndex == 1 && currentMode != GameMode.PlayByPost
            ? ResolveAIGoldIncome(baseIncome, turnNumber)
            : baseIncome;
        if (income > 0)
        {
            AddGoldForSeat(ownerSeatIndex, income);
        }
    }

    private void CollectIncomeForSide(bool sideIsPlayerOwned)
    {
        CollectIncomeForSeat(sideIsPlayerOwned ? 0 : 1);
    }

    private void BeginSideTurn(bool sideIsPlayerOwned, bool playTurnStartSound)
    {
        if (gameOver)
            return;

        autoEndTurnDisabledLoggedThisTurn = false;
        isPlayerTurn = sideIsPlayerOwned;
        currentTurnSeatIndex = sideIsPlayerOwned ? 0 : 1;

        if (playTurnStartSound && SoundManager.Instance != null && !ShouldSuppressAIVsAIAudio())
        {
            SoundManager.Instance.PlayTurnStart();
        }

        ResetRecruitmentForSide(sideIsPlayerOwned);

        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.ResetMovementForSide(sideIsPlayerOwned, IsCurrentSideOwner(sideIsPlayerOwned));
            UnitSelectionManager.Instance.ClearSelection();
        }

        if (TileHoverManager.Instance != null)
        {
            TileHoverManager.Instance.ClearSelection();
        }

        if (CityUIManager.Instance != null)
        {
            CityUIManager.Instance.ClosePanel();
        }

        CollectIncomeForSide(sideIsPlayerOwned);
        RecalculatePlayerVisibility();

        ScheduleAutoEndTurnCheck();
        RefreshEndTurnButtonInteractable(force: true);

        if (currentMode == GameMode.VsAI)
        {
            AutoSaveIfEnabled();
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void ClearAIVsAICompletedTournamentAutoRestartState()
    {
        aiVsAiCompletedTournamentAutoRestartPending = false;
        aiVsAiCompletedTournamentAutoRestartMessage = string.Empty;
    }

    public bool IsAIVsAIDebugModeEnabledForUi()
    {
        return IsAIVsAIDebugModeActive();
    }

    public bool IsAIVsAIDebugPausedForUi()
    {
        return currentMode == GameMode.VsAI && aiVsAiDebugPaused;
    }

    public bool CanToggleAIVsAIDebugPauseForUi()
    {
        return CanUseAIVsAIBatchPauseResumeControlForUi();
    }

    public bool CanCancelAIVsAICompletedTournamentAutoRestartForUi()
    {
        return IsAIVsAICompletedTournamentAutoRestartPendingForUi();
    }

    private bool ShouldUseAIVsAIBatchPauseResumeControlForUi()
    {
        return IsAIVsAIBatchHudVisibleForUi() ||
               (currentMode == GameMode.VsAI &&
                IsAIVsAIDebugModeActive() &&
                (!gameOver || aiVsAiDebugRestartPending));
    }

    private bool CanUseAIVsAIBatchPauseResumeControlForUi()
    {
        if (!ShouldUseAIVsAIBatchPauseResumeControlForUi())
        {
            return false;
        }

        return HasAIVsAIBatchRunActiveForUi() || CanPauseContinuousTournamentBetweenMatchesForUi();
    }

    private BottomRightControlUiState ResolveBottomRightControlForUiInternal(string checkpoint = null, bool forceTrace = false)
    {
        BottomRightControlUiState resolvedState;
        if (IsAIVsAICompletedTournamentAutoRestartPendingForUi())
        {
            resolvedState = new BottomRightControlUiState(
                BottomRightControlKind.BatchStopPendingRestart,
                BottomRightControlBatchStopLabel,
                CanCancelAIVsAICompletedTournamentAutoRestartForUi());
            TraceBottomRightControlState(resolvedState, checkpoint, forceTrace);
            return resolvedState;
        }

        if (IsContinuousTournamentBatchControlActiveForUi() && IsGameOverUiVisible)
        {
            resolvedState = new BottomRightControlUiState(
                BottomRightControlKind.BatchContinueTournament,
                BottomRightControlBatchContinueTournamentLabel,
                GameOverUiTertiaryButtonVisible && GameOverUiTertiaryButtonInteractable);
            TraceBottomRightControlState(resolvedState, checkpoint, forceTrace);
            return resolvedState;
        }

        if (ShouldUseAIVsAIBatchPauseResumeControlForUi())
        {
            bool isPaused = IsAIVsAIDebugPausedForUi();
            resolvedState = new BottomRightControlUiState(
                isPaused ? BottomRightControlKind.BatchResume : BottomRightControlKind.BatchPause,
                isPaused ? BottomRightControlBatchResumeLabel : BottomRightControlBatchPauseLabel,
                CanUseAIVsAIBatchPauseResumeControlForUi());
            TraceBottomRightControlState(resolvedState, checkpoint, forceTrace);
            return resolvedState;
        }

        resolvedState = new BottomRightControlUiState(
            BottomRightControlKind.DefaultNext,
            BottomRightControlNextLabel,
            CanAdvanceTurn());
        TraceBottomRightControlState(resolvedState, checkpoint, forceTrace);
        return resolvedState;
    }

    public BottomRightControlUiState ResolveBottomRightControlForUi()
    {
        return ResolveBottomRightControlForUiInternal();
    }

    public void TraceBottomRightControlCheckpointForUi(string checkpoint)
    {
        ResolveBottomRightControlForUiInternal(checkpoint, forceTrace: true);
    }

    public void OnBottomRightControlPressedForUi()
    {
        BottomRightControlUiState controlState = ResolveBottomRightControlForUi();
        if (!controlState.interactable)
        {
            return;
        }

        switch (controlState.kind)
        {
            case BottomRightControlKind.BatchStopPendingRestart:
                CancelAIVsAICompletedTournamentAutoRestartForUi();
                return;

            case BottomRightControlKind.BatchContinueTournament:
                OnGameOverTertiaryButtonPressed();
                return;

            case BottomRightControlKind.BatchPause:
            case BottomRightControlKind.BatchResume:
                ToggleAIVsAIDebugPause();
                return;

            case BottomRightControlKind.DefaultNext:
            default:
                OnEndTurnButtonPressed();
                return;
        }
    }

    public void ToggleAIVsAIDebugPause()
    {
        if (!HasAIVsAIBatchRunActiveForUi())
        {
            return;
        }

        if (aiVsAiDebugRestartPending)
        {
            if (TryPauseContinuousTournamentBetweenMatchesForUi())
            {
                return;
            }

            return;
        }

        aiVsAiDebugPaused = !aiVsAiDebugPaused;
        AIVsAIBatchRunController.SetPaused(aiVsAiDebugPaused);
        if (aiVsAiDebugPaused)
        {
            TryShowPausedContinuousTournamentSummary();
        }
        else if (aiVsAiPausedTournamentSummaryVisible)
        {
            ResetGameOverUiState();
        }

        RefreshEndTurnButtonInteractable(force: true);
    }

    private bool CanPauseContinuousTournamentBetweenMatchesForUi()
    {
        return currentMode == GameMode.VsAI &&
               gameOver &&
               aiVsAiDebugRestartPending &&
               !IsAIVsAICompletedTournamentAutoRestartPendingForUi() &&
               HasAIVsAIBatchRunActiveForUi() &&
               IsContinuousTournamentBatchControlActiveForUi();
    }

    private bool TryPauseContinuousTournamentBetweenMatchesForUi()
    {
        if (!CanPauseContinuousTournamentBetweenMatchesForUi())
        {
            return false;
        }

        aiVsAiDebugRestartPending = false;
        aiVsAiDebugPaused = true;
        AIVsAIBatchRunController.SetPaused(true);
        TryShowPausedContinuousTournamentSummary();
        RefreshEndTurnButtonInteractable(force: true);
        return true;
    }

    private void TraceBottomRightControlState(
        BottomRightControlUiState state,
        string checkpoint,
        bool forceTrace)
    {
        bool hasHudSnapshot = AIVsAIBatchRunController.TryGetHudSnapshot(out _);
        bool hasActiveRunSnapshot = AIVsAIBatchRunController.TryGetActiveRunSnapshot(out _);
        bool continuousTournamentBatchControlActive = IsContinuousTournamentBatchControlActiveForUi();
        bool shouldTrace =
            hasHudSnapshot ||
            hasActiveRunSnapshot ||
            aiVsAiDebugRestartPending ||
            aiVsAiCompletedTournamentAutoRestartPending ||
            aiVsAiDebugPaused ||
            continuousTournamentBatchControlActive ||
            IsAIVsAIDebugModeActive();
        if (!shouldTrace)
        {
            return;
        }

        string signature =
            $"{state.kind}|{state.label}|{state.interactable}|mode={currentMode}|gameOver={gameOver}|paused={aiVsAiDebugPaused}|restartPending={aiVsAiDebugRestartPending}|completedRestartPending={aiVsAiCompletedTournamentAutoRestartPending}|gameOverUi={IsGameOverUiVisible}|hudSnapshot={hasHudSnapshot}|activeRunSnapshot={hasActiveRunSnapshot}|continuousBatch={continuousTournamentBatchControlActive}";
        if (!forceTrace &&
            string.Equals(lastBottomRightControlTraceSignature, signature, System.StringComparison.Ordinal))
        {
            return;
        }

        lastBottomRightControlTraceSignature = signature;
        string checkpointLabel = string.IsNullOrWhiteSpace(checkpoint) ? "StateChanged" : checkpoint;
        Debug.Log($"[AIVsAIBottomRight] checkpoint={checkpointLabel} {signature}");
    }

    public void CancelAIVsAICompletedTournamentAutoRestartForUi()
    {
        if (!IsAIVsAICompletedTournamentAutoRestartPendingForUi())
        {
            return;
        }

        aiVsAiCompletedTournamentAutoRestartPending = false;
        aiVsAiDebugRestartPending = false;
        ShowGameOverPopup(aiVsAiCompletedTournamentAutoRestartMessage, writeLog: false);
        ClearAIVsAICompletedTournamentAutoRestartState();
        RefreshEndTurnButtonInteractable(force: true);
    }

    private bool TryShowPausedContinuousTournamentSummary()
    {
        if (!IsContinuousTournamentBatchControlActiveForUi() ||
            !AIVsAIBatchRunController.TryGetActiveRunSnapshot(out AIVsAIBatchRunController.ActiveRunSnapshot snapshot) ||
            snapshot.simulationMode != AIVsAIBatchRunController.SimulationMode.Tournament)
        {
            return false;
        }

        aiVsAiPausedTournamentSummaryVisible = true;
        SetGameOverUiTitle(AIVsAITournamentPausedTitle);
        SetGameOverPrimaryCopyTextButtonState("Copy Text", true);
        SetGameOverSecondaryButtonState(string.Empty, visible: false, interactable: false, action: GameOverSecondaryAction.None);
        SetGameOverTertiaryButtonState(
            "Continue Tournament",
            visible: true,
            interactable: true,
            action: GameOverTertiaryAction.ResumeAIVsAIBatch);
        ShowGameOverPopup(BuildPausedTournamentSummaryMessage(snapshot), writeLog: false);
        return true;
    }

    private string BuildPausedTournamentSummaryMessage(AIVsAIBatchRunController.ActiveRunSnapshot snapshot)
    {
        string standingsPreview = string.IsNullOrWhiteSpace(snapshot.tournamentStandingsPreview)
            ? "Standings pending"
            : snapshot.tournamentStandingsPreview;
        string remainingTimeText = snapshot.gamesPerSecond > 0.0001f
            ? FormatDuration(snapshot.remainingTimeSeconds)
            : "Estimating";
        string tournamentTypeLabel = "Tournament";
        if (AIVsAIBatchRunController.TryGetActiveSimulationSettings(out AIVsAIBatchRunController.SimulationSettings activeSettings))
        {
            tournamentTypeLabel = AIVsAIBatchRunController.GetTournamentTypeDisplayName(activeSettings.tournamentType);
        }

        return
            $"Current tournament paused.\n" +
            $"Format: {tournamentTypeLabel}\n" +
            $"Participants: {snapshot.participantCount}\n" +
            $"Pairings: {snapshot.scheduledPairings}\n" +
            $"Completed games: {snapshot.completedGames}/{snapshot.scheduledGames}\n" +
            $"Draws: {snapshot.trueDraws}\n" +
            $"Aborts: {snapshot.aborts}\n" +
            $"Elapsed time: {FormatDuration(snapshot.elapsedSeconds)}\n" +
            $"Estimated time remaining: {remainingTimeText}\n" +
            $"Games/sec: {snapshot.gamesPerSecond:0.00}\n" +
            $"Standings preview:\n{standingsPreview}";
    }
#else
    public bool IsAIVsAIDebugModeEnabledForUi() => false;
    public bool IsAIVsAIDebugPausedForUi() => false;
    public bool CanToggleAIVsAIDebugPauseForUi() => false;
    public bool CanCancelAIVsAICompletedTournamentAutoRestartForUi() => false;
    public BottomRightControlUiState ResolveBottomRightControlForUi() =>
        new BottomRightControlUiState(BottomRightControlKind.DefaultNext, BottomRightControlNextLabel, CanAdvanceTurn());
    public void TraceBottomRightControlCheckpointForUi(string checkpoint) { }
    public void OnBottomRightControlPressedForUi() => OnEndTurnButtonPressed();
    public void ToggleAIVsAIDebugPause() { }
    public void CancelAIVsAICompletedTournamentAutoRestartForUi() { }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private float GetAIVsAIDebugTurnDelaySeconds()
    {
        float defaultDelaySeconds = Mathf.Max(0f, aiTurnDelay);
        switch (aiVsAiBatchSpeedPreset)
        {
            case AIVsAIBatchSpeedPreset.Fast:
                return Mathf.Min(defaultDelaySeconds, AIVsAIFastTurnDelaySeconds);

            case AIVsAIBatchSpeedPreset.VeryFast:
                return Mathf.Min(defaultDelaySeconds, AIVsAIVeryFastTurnDelaySeconds);

            case AIVsAIBatchSpeedPreset.UltraFast:
                return AIVsAIUltraFastTurnDelaySeconds;

            case AIVsAIBatchSpeedPreset.Normal:
            default:
                return defaultDelaySeconds;
        }
    }

    private float GetAIVsAIDebugRestartDelaySeconds()
    {
        switch (aiVsAiBatchSpeedPreset)
        {
            case AIVsAIBatchSpeedPreset.Fast:
                return AIVsAIFastRestartDelaySeconds;

            case AIVsAIBatchSpeedPreset.VeryFast:
                return AIVsAIVeryFastRestartDelaySeconds;

            case AIVsAIBatchSpeedPreset.UltraFast:
                return AIVsAIUltraFastRestartDelaySeconds;

            case AIVsAIBatchSpeedPreset.Normal:
            default:
                return 1.25f;
        }
    }

    private bool TryHandleAIVsAIBatchTurnLimitReached()
    {
        if (!IsAIVsAIBatchModeActive() || turnNumber <= AIVsAIBatchTurnLimit)
        {
            return false;
        }

        return TryHandleAbortedAIVsAIDebugMatch($"TurnLimit:{AIVsAIBatchTurnLimit}");
    }
#else
    private float GetAIVsAIDebugTurnDelaySeconds()
    {
        return Mathf.Max(0f, aiTurnDelay);
    }

    private float GetAIVsAIDebugRestartDelaySeconds()
    {
        return 1.25f;
    }

    private bool TryHandleAIVsAIBatchTurnLimitReached()
    {
        return false;
    }
#endif

    private void AdvanceVsAITurnAfterSide(bool completedSideWasPlayerOwned)
    {
        if (completedSideWasPlayerOwned)
        {
            isPlayerTurn = false;
            return;
        }

        turnNumber++;
        isPlayerTurn = true;
    }

    private void StartAIVsAIDebugLoopIfNeeded()
    {
        if (!IsAIVsAIDebugModeActive() || currentMode != GameMode.VsAI || gameOver)
            return;

        if (aiVsAiDebugRoutine != null)
            return;

        aiVsAiDebugRoutine = StartCoroutine(RunAIVsAIDebugLoop());
    }

    private IEnumerator RunAIVsAIDebugLoop()
    {
        try
        {
            while (currentMode == GameMode.VsAI && IsAIVsAIDebugModeActive() && !gameOver)
            {
                while (aiVsAiDebugPaused && currentMode == GameMode.VsAI && IsAIVsAIDebugModeActive() && !gameOver)
                {
                    yield return null;
                }

                if (TryHandleAIVsAIBatchTurnLimitReached())
                {
                    yield break;
                }

                bool actingSideIsPlayerOwned = isPlayerTurn;
                float delaySeconds = GetAIVsAIDebugTurnDelaySeconds();
                if (delaySeconds > 0f)
                {
                    yield return new WaitForSecondsRealtime(delaySeconds);
                }

                while (aiVsAiDebugPaused && currentMode == GameMode.VsAI && IsAIVsAIDebugModeActive() && !gameOver)
                {
                    yield return null;
                }

                if (currentMode != GameMode.VsAI || !IsAIVsAIDebugModeActive() || gameOver)
                    yield break;

                try
                {
                    TryCaptureAIDecisionSnapshot(actingSideIsPlayerOwned);
                    RunAITurnForSide(actingSideIsPlayerOwned);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(
                        $"[AIVsAI] Turn execution failed. actingSideIsPlayerOwned={actingSideIsPlayerOwned} " +
                        $"turnNumber={turnNumber} aiConfig={BuildAIConfigLabelForSide(actingSideIsPlayerOwned)} " +
                        $"sideARecruitVariant={aiVsAiSideARecruitVariant} sideBRecruitVariant={aiVsAiSideBRecruitVariant}");
                    Debug.LogException(ex);
                    if (TryHandleAbortedAIVsAIDebugMatch($"Exception: {ex.GetType().Name}"))
                    {
                        yield break;
                    }
                }

                if (gameOver)
                    yield break;

                AdvanceVsAITurnAfterSide(actingSideIsPlayerOwned);
                if (gameOver)
                    yield break;

                BeginSideTurn(isPlayerTurn, playTurnStartSound: true);
                yield return null;
            }
        }
        finally
        {
            aiVsAiDebugRoutine = null;
        }
    }

    private bool IsAIVsAIDebugModeActive()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return currentMode == GameMode.VsAI && enableAIVsAIDebugMode;
#else
        return false;
#endif
    }

    public bool ShouldSuppressAIVsAIAudio()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return IsAIVsAIDebugModeActive() && aiVsAiBatchSpeedPreset == AIVsAIBatchSpeedPreset.UltraFast;
#else
        return false;
#endif
    }

    private bool ShouldSkipAIVsAISnapshotHistory()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return currentMode == GameMode.VsAI &&
               IsAIVsAIDebugModeActive() &&
               IsAIVsAIBatchModeActive() &&
               aiVsAiBatchSpeedPreset == AIVsAIBatchSpeedPreset.UltraFast;
#else
        return false;
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void ResolveAIVsAIDebugRuntimeSettings(bool consumePendingSelection)
    {
        aiVsAiSideARecruitVariant = aiRecruitVariant;
        aiVsAiSideBRecruitVariant = aiRecruitVariant;
        aiVsAiSideAFeatures = aiLocalDecisionFeatures;
        aiVsAiSideBFeatures = aiLocalDecisionFeatures;
        aiVsAiSideAProfile = AIDebugProfile.Baseline;
        aiVsAiSideBProfile = AIDebugProfile.Baseline;
        aiVsAiBatchSpeedPreset = AIVsAIBatchSpeedPreset.Normal;

        if (currentMode != GameMode.VsAI || string.IsNullOrWhiteSpace(currentGameId))
        {
            enableAIVsAIDebugMode = false;
            aiVsAiDebugRestartPending = false;
            ClearAIVsAICompletedTournamentAutoRestartState();
            return;
        }

        if (consumePendingSelection)
        {
            if (AIVsAIDebugSelection.TryConsume(out AIVsAIDebugSelection.Settings pendingSettings))
            {
                ApplyAIVsAIDebugRuntimeSettings(pendingSettings);
                AIVsAIDebugSelection.SaveForGame(currentGameId, pendingSettings);
                return;
            }

            if (enableAIVsAIDebugMode)
            {
                AIVsAIDebugSelection.SaveForGame(currentGameId, new AIVsAIDebugSelection.Settings
                {
                    enabled = true,
                    sideARecruitVariant = aiVsAiSideARecruitVariant,
                    sideBRecruitVariant = aiVsAiSideBRecruitVariant,
                    sideAFeatures = aiVsAiSideAFeatures,
                    sideBFeatures = aiVsAiSideBFeatures,
                    sideAProfile = aiVsAiSideAProfile,
                    sideBProfile = aiVsAiSideBProfile,
                    batchSpeedPreset = aiVsAiBatchSpeedPreset
                });
            }

            return;
        }

        enableAIVsAIDebugMode = false;
        if (AIVsAIDebugSelection.TryLoadForGame(currentGameId, out AIVsAIDebugSelection.Settings persistedSettings))
        {
            ApplyAIVsAIDebugRuntimeSettings(persistedSettings);
        }
    }

    private void ResolveAIVsAIBatchRuntimeSettings(bool consumePendingSelection)
    {
        if (!consumePendingSelection)
        {
            return;
        }

        if (!AIVsAIBatchRunController.TryConsumePendingSimulationSettings(
                out AIVsAIBatchRunController.SimulationSettings simulationSettings))
        {
            return;
        }

        if (currentMode == GameMode.VsAI && enableAIVsAIDebugMode)
        {
            if (AIVsAIBatchRunController.HasActiveRun)
            {
                return;
            }

            AIVsAIBatchRunController.BeginNewRun(
                simulationSettings,
                aiVsAiBatchSpeedPreset,
                aiVsAiSideARecruitVariant,
                aiVsAiSideBRecruitVariant,
                aiVsAiSideAFeatures,
                aiVsAiSideBFeatures,
                aiVsAiSideAProfile,
                aiVsAiSideBProfile);
            return;
        }

        AIVsAIBatchRunController.ClearAll();
    }

    private void ApplyAIVsAIDebugRuntimeSettings(AIVsAIDebugSelection.Settings settings)
    {
        enableAIVsAIDebugMode = settings.enabled;
        aiVsAiSideARecruitVariant = settings.sideARecruitVariant;
        aiVsAiSideBRecruitVariant = settings.sideBRecruitVariant;
        aiVsAiSideAFeatures = settings.sideAFeatures;
        aiVsAiSideBFeatures = settings.sideBFeatures;
        aiVsAiSideAProfile = settings.sideAProfile;
        aiVsAiSideBProfile = settings.sideBProfile;
        aiVsAiBatchSpeedPreset = settings.batchSpeedPreset;
        aiVsAiDebugPaused = false;
        aiVsAiDebugRestartPending = false;
        ClearAIVsAICompletedTournamentAutoRestartState();
    }
#endif

    private AIRecruitVariant GetAIRecruitVariantForSide(bool actingSideIsPlayerOwned)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsAIVsAIDebugModeActive())
        {
            return actingSideIsPlayerOwned ? aiVsAiSideARecruitVariant : aiVsAiSideBRecruitVariant;
        }
#endif
        return aiRecruitVariant;
    }

    private static AILocalDecisionFeatures GetPlayVsAIPresetFeatures(AIRecruitVariant recruitVariant)
    {
        switch (recruitVariant)
        {
            case AIRecruitVariant.RiderFocus:
                return AILocalDecisionFeatures.OffensiveObviousWin;

            case AIRecruitVariant.Default:
            default:
                return AILocalDecisionFeatures.OffensiveObviousWin |
                       AILocalDecisionFeatures.ExchangeScoring |
                       AILocalDecisionFeatures.DefensiveVeto;
        }
    }

    private AILocalDecisionFeatures GetAILocalDecisionFeaturesForSide(bool actingSideIsPlayerOwned)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsAIVsAIDebugModeActive())
        {
            return actingSideIsPlayerOwned ? aiVsAiSideAFeatures : aiVsAiSideBFeatures;
        }
#endif
        if (currentMode == GameMode.VsAI)
        {
            return GetPlayVsAIPresetFeatures(aiRecruitVariant);
        }

        return aiLocalDecisionFeatures;
    }

    private AIDebugProfile GetAIDebugProfileForSide(bool actingSideIsPlayerOwned)
    {
        return AIDebugProfile.Baseline;
    }

    private string BuildAIConfigLabelForSide(bool actingSideIsPlayerOwned)
    {
        return $"recruitVariant={GetAIRecruitVariantForSide(actingSideIsPlayerOwned)};localFeatures={AIPostCalculusLocalDecisionHelper.ToConfigValue(GetAILocalDecisionFeaturesForSide(actingSideIsPlayerOwned))};profile={GetAIDebugProfileForSide(actingSideIsPlayerOwned)}";
    }

    private bool TryExecuteImmediateCityWin(
        Unit unit,
        City[] allCities,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo,
        float tileSize)
    {
        if (unit == null ||
            allCities == null ||
            gridManager == null ||
            !AIPostCalculusLocalDecisionHelper.HasFeature(
                GetAILocalDecisionFeaturesForSide(unit.isPlayerOwned),
                AILocalDecisionFeatures.OffensiveObviousWin))
        {
            return false;
        }

        if (!gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility unitTile) || unitTile == null)
        {
            return false;
        }

        List<AIPostCalculusLocalDecisionHelper.ImmediateCityWinCandidate> candidates =
            new List<AIPostCalculusLocalDecisionHelper.ImmediateCityWinCandidate>();

        for (int cityIndex = 0; cityIndex < allCities.Length; cityIndex++)
        {
            City city = allCities[cityIndex];
            if (city == null || city.isPlayerOwned == unit.isPlayerOwned)
            {
                continue;
            }

            if (!IsCityTileVisibleToSide(city, visibleTiles, aiHasPerfectInfo))
            {
                continue;
            }

            int distanceToCity = Mathf.Max(Mathf.Abs(unitTile.gridX - city.x), Mathf.Abs(unitTile.gridY - city.y));
            if (distanceToCity != 1)
            {
                continue;
            }

            Unit visibleOccupant = GetPerceivedEnemyUnitAtCity(city, unit.isPlayerOwned, visibleTiles, aiHasPerfectInfo);
            if (visibleOccupant == null)
            {
                if (unit.CanMoveThisTurn())
                {
                    candidates.Add(new AIPostCalculusLocalDecisionHelper.ImmediateCityWinCandidate(
                        city.transform.position,
                        city.x,
                        city.y,
                        requiresAttack: false));
                }

                continue;
            }

            if (!unit.CanMoveThisTurn() ||
                !unit.CanAttackThisTurn() ||
                unit.AttackRange > 1 ||
                !unit.AdvancesIntoDefenderTileOnKill)
            {
                continue;
            }

            int predictedDamage = Mathf.Max(0, unit.attackUnits - visibleOccupant.defenseUnits);
            if (predictedDamage < visibleOccupant.currentHealthUnits)
            {
                continue;
            }

            candidates.Add(new AIPostCalculusLocalDecisionHelper.ImmediateCityWinCandidate(
                city.transform.position,
                city.x,
                city.y,
                requiresAttack: true));
        }

        if (!AIPostCalculusLocalDecisionHelper.TryChooseImmediateCityWin(
                GetAILocalDecisionFeaturesForSide(unit.isPlayerOwned),
                candidates,
                out AIPostCalculusLocalDecisionHelper.ImmediateCityWinCandidate chosenCandidate))
        {
            return false;
        }

        if (chosenCandidate.requiresAttack)
        {
            MoveAIUnitOneStep(unit, chosenCandidate.targetPosition, tileSize, aiHasPerfectInfo);
            return gameOver;
        }

        return TryExecuteImmediateEmptyCityCapture(unit, chosenCandidate.targetPosition);
    }

    private bool TryExecuteImmediateEmptyCityCapture(Unit unit, Vector3 targetPosition)
    {
        if (unit == null || !unit.CanMoveThisTurn())
        {
            return false;
        }

        City targetCity = GridUtils.GetCityAtPosition(targetPosition);
        if (targetCity == null || targetCity.isPlayerOwned == unit.isPlayerOwned)
        {
            return false;
        }

        Unit occupyingUnit = GridUtils.GetUnitAtPosition(targetPosition, unit);
        if (occupyingUnit != null)
        {
            return false;
        }

        if (unit.currentCity != null)
        {
            unit.currentCity.stationedUnit = null;
            unit.currentCity = null;
        }

        Vector3 destination = targetPosition;
        destination.z = unit.transform.position.z;
        unit.transform.position = destination;
        unit.RegisterMove();

        if (SoundManager.Instance != null && !ShouldSuppressAIVsAIAudio())
        {
            SoundManager.Instance.PlayMove();
        }

        OnCityCaptured(unit.ownerSeatIndex, targetCity);
        return gameOver;
    }

    private bool IsCityTileVisibleToSide(City city, HashSet<TileVisibility> visibleTiles, bool aiHasPerfectInfo)
    {
        if (city == null)
        {
            return false;
        }

        if (aiHasPerfectInfo)
        {
            return true;
        }

        return gridManager != null &&
               visibleTiles != null &&
               gridManager.TryGetTile(city.x, city.y, out TileVisibility cityTile) &&
               cityTile != null &&
               visibleTiles.Contains(cityTile);
    }

    private Unit SelectBestLocalAttackTarget(Unit attacker, IList<Unit> potentialTargets)
    {
        if (attacker == null || potentialTargets == null || gridManager == null)
        {
            return null;
        }

        if (!gridManager.TryGetTileAtWorldPosition(attacker.transform.position, out TileVisibility attackerTile) || attackerTile == null)
        {
            return null;
        }

        List<AIPostCalculusLocalDecisionHelper.AttackCandidate> candidates =
            new List<AIPostCalculusLocalDecisionHelper.AttackCandidate>();

        for (int i = 0; i < potentialTargets.Count; i++)
        {
            Unit target = potentialTargets[i];
            if (target == null || target.isPlayerOwned == attacker.isPlayerOwned || !target.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!gridManager.TryGetTileAtWorldPosition(target.transform.position, out TileVisibility targetTile) || targetTile == null)
            {
                continue;
            }

            int tileDistance = Mathf.Max(
                Mathf.Abs(targetTile.gridX - attackerTile.gridX),
                Mathf.Abs(targetTile.gridY - attackerTile.gridY));
            if (!attacker.IsTargetInAttackRange(tileDistance))
            {
                continue;
            }

            City targetCity = GridUtils.GetCityAtPosition(target.transform.position);
            int predictedDamage = Mathf.Max(0, attacker.attackUnits - target.defenseUnits);
            candidates.Add(new AIPostCalculusLocalDecisionHelper.AttackCandidate(
                target,
                canKill: predictedDamage >= target.currentHealthUnits,
                predictedDamage: predictedDamage,
                baselineDistance: tileDistance,
                baselineTargetHealth: target.currentHealthUnits,
                targetAttackUnits: target.attackUnits,
                targetOccupiesEnemyCity: targetCity != null && targetCity.isPlayerOwned != attacker.isPlayerOwned));
        }

        return AIPostCalculusLocalDecisionHelper.ChooseAttackTarget(
            GetAILocalDecisionFeaturesForSide(attacker.isPlayerOwned),
            candidates);
    }

    private List<Unit> BuildPerceivedEnemyUnitsForSide(
        bool actingSideIsPlayerOwned,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        List<Unit> perceivedEnemyUnits = new List<Unit>();
        Unit[] allUnits = Object.FindObjectsByType<Unit>();
        for (int i = 0; i < allUnits.Length; i++)
        {
            Unit unit = allUnits[i];
            if (unit == null ||
                unit.isPlayerOwned == actingSideIsPlayerOwned ||
                !unit.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (aiHasPerfectInfo)
            {
                perceivedEnemyUnits.Add(unit);
                continue;
            }

            if (gridManager == null ||
                visibleTiles == null ||
                !gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile) ||
                tile == null ||
                !visibleTiles.Contains(tile))
            {
                continue;
            }

            perceivedEnemyUnits.Add(unit);
        }

        return perceivedEnemyUnits;
    }

    private bool WouldMoveCandidateExposeVisibleImmediateThreatToKeyCity(
        Unit unit,
        Vector3 candidatePosition,
        City keyCity,
        Unit[] allUnits,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        if (unit == null || keyCity == null || allUnits == null)
        {
            return false;
        }

        Vector3 originalPosition = unit.transform.position;
        City originCity = unit.currentCity;
        GameObject originCityStationedUnit = originCity != null ? originCity.stationedUnit : null;
        City destinationCity = GridUtils.GetCityAtPosition(candidatePosition);
        GameObject destinationCityStationedUnit = destinationCity != null ? destinationCity.stationedUnit : null;

        try
        {
            if (originCity != null && originCity.stationedUnit == unit.gameObject)
            {
                originCity.stationedUnit = null;
            }

            unit.currentCity = null;
            unit.transform.position = candidatePosition;

            if (destinationCity != null && destinationCity.isPlayerOwned == unit.isPlayerOwned)
            {
                destinationCity.stationedUnit = unit.gameObject;
                unit.currentCity = destinationCity;
            }

            return CountImmediateThreatSourcesNearCity(
                       keyCity,
                       allUnits,
                       unit.isPlayerOwned,
                       visibleTiles,
                       aiHasPerfectInfo) > 0;
        }
        finally
        {
            unit.transform.position = originalPosition;
            unit.currentCity = originCity;

            if (destinationCity != null)
            {
                destinationCity.stationedUnit = destinationCityStationedUnit;
            }

            if (originCity != null)
            {
                originCity.stationedUnit = originCityStationedUnit;
            }
        }
    }

    private Unit GetPerceivedEnemyUnitAtCity(
        City city,
        bool actingSideIsPlayerOwned,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        if (city == null)
        {
            return null;
        }

        Unit occupant = GridUtils.GetUnitAtPosition(city.transform.position);
        if (occupant == null || occupant.isPlayerOwned == actingSideIsPlayerOwned)
        {
            return null;
        }

        if (aiHasPerfectInfo)
        {
            return occupant;
        }

        if (gridManager == null ||
            visibleTiles == null ||
            !gridManager.TryGetTile(city.x, city.y, out TileVisibility cityTile) ||
            cityTile == null ||
            !visibleTiles.Contains(cityTile))
        {
            return null;
        }

        return occupant;
    }

    private int CountImmediateThreatSourcesNearCity(
        City city,
        Unit[] allUnits,
        bool actingSideIsPlayerOwned,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        if (city == null || allUnits == null || gridManager == null)
        {
            return 0;
        }

        int threatSourceCount = 0;
        for (int unitIndex = 0; unitIndex < allUnits.Length; unitIndex++)
        {
            Unit enemyUnit = allUnits[unitIndex];
            if (!CanThreatenCityNextTurn(enemyUnit, city, actingSideIsPlayerOwned, visibleTiles, aiHasPerfectInfo))
            {
                continue;
            }

            threatSourceCount++;
        }

        return threatSourceCount;
    }

    private bool CanThreatenCityNextTurn(
        Unit enemyUnit,
        City city,
        bool actingSideIsPlayerOwned,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        if (enemyUnit == null ||
            city == null ||
            enemyUnit.isPlayerOwned == actingSideIsPlayerOwned ||
            !enemyUnit.gameObject.activeInHierarchy ||
            gridManager == null)
        {
            return false;
        }

        if (!aiHasPerfectInfo)
        {
            if (visibleTiles == null || visibleTiles.Count == 0)
            {
                return false;
            }

            if (!gridManager.TryGetTileAtWorldPosition(enemyUnit.transform.position, out TileVisibility enemyTile) ||
                enemyTile == null ||
                !visibleTiles.Contains(enemyTile))
            {
                return false;
            }
        }

        if (!gridManager.TryGetTileAtWorldPosition(enemyUnit.transform.position, out TileVisibility sourceTile) || sourceTile == null)
        {
            return false;
        }

        int distanceToCity = Mathf.Max(Mathf.Abs(sourceTile.gridX - city.x), Mathf.Abs(sourceTile.gridY - city.y));
        if (distanceToCity > enemyUnit.maxMovesPerTurn)
        {
            return false;
        }

        Unit defender = GridUtils.GetUnitAtPosition(city.transform.position);
        if (defender == null || defender.isPlayerOwned != actingSideIsPlayerOwned)
        {
            return true;
        }

        if (enemyUnit.AttackRange > 1)
        {
            int predictedDamage = Mathf.Max(0, enemyUnit.attackUnits - defender.defenseUnits);
            return distanceToCity <= enemyUnit.maxMovesPerTurn + enemyUnit.AttackRange &&
                   predictedDamage >= defender.currentHealthUnits;
        }

        if (distanceToCity != 1 && distanceToCity > enemyUnit.maxMovesPerTurn)
        {
            return false;
        }

        int meleeDamage = Mathf.Max(0, enemyUnit.attackUnits - defender.defenseUnits);
        return meleeDamage >= defender.currentHealthUnits;
    }

    private void RunAIForSide(bool actingSideIsPlayerOwned)
    {
        bool enemyIsPlayerOwned = !actingSideIsPlayerOwned;
        City[] allCities = Object.FindObjectsByType<City>();
        Unit[] unitsBeforeRecruitment = Object.FindObjectsByType<Unit>();
        const bool aiHasPerfectInfo = false;
        HashSet<TileVisibility> aiVisibleTiles = ComputeVisibilityForSide(actingSideIsPlayerOwned);
        City primaryControlledCity = FindPrimaryControlledCity(allCities, actingSideIsPlayerOwned);

        // 1) Recruit from each controlled city (one unit per city per turn, if the city is empty)
        foreach (City city in allCities)
        {
            if (city == null)
                continue;

            if (city.isPlayerOwned == actingSideIsPlayerOwned && city.CanRecruit())
            {
                if (TryRecruitVariantOverride(city))
                {
                }
                else
                {
                    TryRecruitBaselineUnit(city, actingSideIsPlayerOwned, allCities, unitsBeforeRecruitment, aiVisibleTiles);
                }
            }
        }

        // 2) Move controlled units toward the nearest enemy unit or city.
        Unit[] allUnits = Object.FindObjectsByType<Unit>();

        List<Vector3> enemyTargets = new List<Vector3>();
        List<Vector3> enemyCityPositions = new List<Vector3>();
        List<Unit> visibleEnemyUnits = new List<Unit>();

        foreach (Unit unit in allUnits)
        {
            if (unit == null || unit.isPlayerOwned != enemyIsPlayerOwned)
                continue;

            if (gridManager != null &&
                gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile) &&
                aiVisibleTiles.Contains(tile))
            {
                enemyTargets.Add(unit.transform.position);
                visibleEnemyUnits.Add(unit);
            }
        }

        foreach (City city in allCities)
        {
            if (city == null || city.isPlayerOwned != enemyIsPlayerOwned)
                continue;

            Vector3 cityPos = city.transform.position;
            enemyTargets.Add(cityPos);
            enemyCityPositions.Add(cityPos);
        }

        if (enemyTargets.Count == 0)
        {
            return;
        }

        float stepSize = gridManager != null ? Mathf.Max(0.01f, gridManager.tileSize) : 1f;

        Dictionary<City, AITurnLogic.CityDefensePlan> cityDefensePlans = AITurnLogic.BuildCityDefensePlans(
            gridManager,
            allCities,
            allUnits,
            primaryControlledCity,
            actingSideIsPlayerOwned,
            aiVisibleTiles,
            aiHasPerfectInfo);
        Dictionary<Unit, AITurnLogic.CityDefensePlan> combatDefenseAssignments = new Dictionary<Unit, AITurnLogic.CityDefensePlan>();
        Dictionary<Unit, AITurnLogic.CityDefensePlan> scoutDefenseAssignments = new Dictionary<Unit, AITurnLogic.CityDefensePlan>();
        foreach (KeyValuePair<City, AITurnLogic.CityDefensePlan> entry in cityDefensePlans)
        {
            AITurnLogic.CityDefensePlan plan = entry.Value;
            AddDefenseAssignmentsForPlan(plan, combatDefenseAssignments, scoutDefenseAssignments);
        }

        foreach (Unit unit in allUnits)
        {
            if (unit == null || unit.isPlayerOwned != actingSideIsPlayerOwned)
                continue;

            unit.ResetMovementForTurn();

            if (TryExecuteImmediateCityWin(unit, allCities, aiVisibleTiles, aiHasPerfectInfo, stepSize))
            {
                if (gameOver)
                    return;
                continue;
            }

            if (combatDefenseAssignments.TryGetValue(unit, out AITurnLogic.CityDefensePlan combatPlan))
            {
                if (ExecuteAssignedCityDefense(unit, combatPlan, stepSize, primaryControlledCity, allUnits, aiVisibleTiles, aiHasPerfectInfo))
                {
                    if (gameOver)
                        return;
                    continue;
                }
            }

            if (scoutDefenseAssignments.TryGetValue(unit, out AITurnLogic.CityDefensePlan scoutPlan))
            {
                if (ExecuteAssignedCityDefense(unit, scoutPlan, stepSize, primaryControlledCity, allUnits, aiVisibleTiles, aiHasPerfectInfo))
                {
                    if (gameOver)
                        return;
                    continue;
                }
            }

            ExecuteBaselineUnitTurn(unit, visibleEnemyUnits, enemyTargets, enemyCityPositions, stepSize, primaryControlledCity, allUnits, aiVisibleTiles, aiHasPerfectInfo);
            if (gameOver)
                return;
        }
    }

    private void TryRecruitBaselineUnit(
        City city,
        bool actingSideIsPlayerOwned,
        City[] allCities,
        Unit[] existingUnits,
        HashSet<TileVisibility> aiVisibleTiles)
    {
        if (city == null)
            return;

        int warriorCount = 0;
        int scoutCount = 0;
        int riderCount = 0;
        int archerCount = 0;
        int controlledCityCount = 0;

        for (int i = 0; i < allCities.Length; i++)
        {
            City otherCity = allCities[i];
            if (otherCity != null && otherCity.isPlayerOwned == actingSideIsPlayerOwned)
            {
                controlledCityCount++;
            }
        }

        for (int i = 0; i < existingUnits.Length; i++)
        {
            Unit existingUnit = existingUnits[i];
            if (existingUnit == null || existingUnit.isPlayerOwned != actingSideIsPlayerOwned)
                continue;

            switch (existingUnit.UnitTypeId)
            {
                case UnitRegistry.ScoutTypeId:
                    scoutCount++;
                    break;
                case UnitRegistry.RiderTypeId:
                    riderCount++;
                    break;
                case UnitRegistry.ArcherTypeId:
                    archerCount++;
                    break;
                default:
                    warriorCount++;
                    break;
            }
        }

        bool cityThreatened = false;
        if (gridManager != null)
        {
            for (int i = 0; i < existingUnits.Length; i++)
            {
                Unit enemyUnit = existingUnits[i];
                if (enemyUnit == null || enemyUnit.isPlayerOwned == actingSideIsPlayerOwned)
                    continue;

                if (aiVisibleTiles == null || aiVisibleTiles.Count == 0)
                {
                    continue;
                }

                if (!gridManager.TryGetTileAtWorldPosition(enemyUnit.transform.position, out TileVisibility enemyTile) ||
                    !aiVisibleTiles.Contains(enemyTile))
                {
                    continue;
                }

                if (!gridManager.TryGetTileAtWorldPosition(enemyUnit.transform.position, out TileVisibility enemyPositionTile))
                    continue;

                int cityThreatDistance = Mathf.Max(
                    Mathf.Abs(enemyPositionTile.gridX - city.x),
                    Mathf.Abs(enemyPositionTile.gridY - city.y));

                if (cityThreatDistance <= 2)
                {
                    cityThreatened = true;
                    break;
                }
            }
        }

        List<string> recruitPriority = new List<string>(4);
        if (cityThreatened)
        {
            recruitPriority.Add(UnitRegistry.WarriorTypeId);
            recruitPriority.Add(UnitRegistry.ArcherTypeId);
            recruitPriority.Add(UnitRegistry.RiderTypeId);
            recruitPriority.Add(UnitRegistry.ScoutTypeId);
        }
        else if (warriorCount == 0)
        {
            recruitPriority.Add(UnitRegistry.WarriorTypeId);
            recruitPriority.Add(UnitRegistry.ScoutTypeId);
            recruitPriority.Add(UnitRegistry.ArcherTypeId);
            recruitPriority.Add(UnitRegistry.RiderTypeId);
        }
        else if (scoutCount == 0)
        {
            recruitPriority.Add(UnitRegistry.ScoutTypeId);
            recruitPriority.Add(UnitRegistry.ArcherTypeId);
            recruitPriority.Add(UnitRegistry.RiderTypeId);
            recruitPriority.Add(UnitRegistry.WarriorTypeId);
        }
        else if (archerCount == 0)
        {
            recruitPriority.Add(UnitRegistry.ArcherTypeId);
            recruitPriority.Add(UnitRegistry.RiderTypeId);
            recruitPriority.Add(UnitRegistry.WarriorTypeId);
            recruitPriority.Add(UnitRegistry.ScoutTypeId);
        }
        else if (riderCount == 0)
        {
            recruitPriority.Add(UnitRegistry.RiderTypeId);
            recruitPriority.Add(UnitRegistry.WarriorTypeId);
            recruitPriority.Add(UnitRegistry.ArcherTypeId);
            recruitPriority.Add(UnitRegistry.ScoutTypeId);
        }
        else
        {
            int scoutCap = controlledCityCount >= 3 ? 2 : 1;
            if (scoutCount < scoutCap)
            {
                recruitPriority.Add(UnitRegistry.ScoutTypeId);
            }

            if (archerCount < Mathf.Max(1, warriorCount / 3))
            {
                recruitPriority.Add(UnitRegistry.ArcherTypeId);
            }

            if (riderCount < Mathf.Max(1, warriorCount / 3))
            {
                recruitPriority.Add(UnitRegistry.RiderTypeId);
            }

            recruitPriority.Add(UnitRegistry.WarriorTypeId);
            recruitPriority.Add(UnitRegistry.ArcherTypeId);
            recruitPriority.Add(UnitRegistry.RiderTypeId);
            recruitPriority.Add(UnitRegistry.ScoutTypeId);
        }

        for (int i = 0; i < recruitPriority.Count; i++)
        {
            if (city.TrySpawnUnit(recruitPriority[i]))
            {
                return;
            }
        }
    }

    private bool TryRecruitVariantOverride(City city)
    {
        if (city == null || GetAIRecruitVariantForSide(city.isPlayerOwned) != AIRecruitVariant.RiderFocus)
        {
            return false;
        }

        return city.TrySpawnUnit(UnitRegistry.RiderTypeId);
    }

    private void ExecuteBaselineUnitTurn(
        Unit unit,
        List<Unit> visibleEnemyUnits,
        List<Vector3> enemyTargets,
        List<Vector3> enemyCityPositions,
        float tileSize,
        City keyCity,
        Unit[] allUnits,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        if (unit == null)
            return;

        string unitTypeId = unit.UnitTypeId;
        if (unitTypeId == UnitRegistry.ScoutTypeId)
        {
            Vector3? scoutTarget = FindNearestTargetPosition(unit.transform.position, enemyCityPositions.Count > 0 ? enemyCityPositions : enemyTargets);
            if (scoutTarget.HasValue)
            {
                MoveAIUnitTowardEmptyTile(unit, scoutTarget.Value, tileSize, keyCity, allUnits, visibleTiles, aiHasPerfectInfo);
            }
            return;
        }

        if (TryAttackVisibleEnemyFromCurrentPosition(unit, visibleEnemyUnits))
        {
            return;
        }

        if (unitTypeId == UnitRegistry.ArcherTypeId)
        {
            Vector3? archerTarget = FindNearestTargetPosition(unit.transform.position, visibleEnemyUnits.Count > 0 ? BuildUnitPositionList(visibleEnemyUnits) : enemyCityPositions);
            if (!archerTarget.HasValue)
            {
                archerTarget = FindNearestTargetPosition(unit.transform.position, enemyTargets);
            }

            if (archerTarget.HasValue)
            {
                MoveAIUnitTowardEmptyTile(unit, archerTarget.Value, tileSize, keyCity, allUnits, visibleTiles, aiHasPerfectInfo);
            }
            return;
        }

        int maxBaselineMoveActions = unitTypeId == UnitRegistry.RiderTypeId ? Mathf.Max(1, unit.GetRemainingMoveRangeThisTurn()) : 1;
        for (int moveIndex = 0; moveIndex < maxBaselineMoveActions && unit.CanMoveThisTurn(); moveIndex++)
        {
            if (TryAttackVisibleEnemyFromCurrentPosition(unit, visibleEnemyUnits))
            {
                return;
            }

            Vector3? chosenTarget = FindNearestTargetPosition(unit.transform.position, enemyTargets);
            if (!chosenTarget.HasValue)
            {
                return;
            }

            Vector3 startPosition = unit.transform.position;
            int attacksUsedBeforeMove = unit.attacksUsedThisTurn;
            MoveAIUnitOneStep(unit, chosenTarget.Value, tileSize, aiHasPerfectInfo);
            if (gameOver)
                return;

            if (unit.attacksUsedThisTurn > attacksUsedBeforeMove)
            {
                return;
            }

            if ((unit.transform.position - startPosition).sqrMagnitude < 0.0001f)
            {
                return;
            }
        }
    }

    private bool ExecuteAssignedCityDefense(
        Unit unit,
        AITurnLogic.CityDefensePlan plan,
        float tileSize,
        City keyCity,
        Unit[] allUnits,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        if (unit == null || plan == null || plan.City == null)
        {
            return false;
        }

        City currentCity = GridUtils.GetCityAtPosition(unit.transform.position);
        bool isOnProtectedCity = currentCity == plan.City;

        if (TryExecuteAssignedCombatDefense(unit, plan.AssignedCombatUnit, plan.PreferredCombatPosition, plan, isOnProtectedCity, tileSize, keyCity, allUnits, visibleTiles, aiHasPerfectInfo))
        {
            return true;
        }

        if (TryExecuteAssignedCombatDefense(unit, plan.AssignedSupportCombatUnit, plan.PreferredSupportCombatPosition, plan, isOnProtectedCity, tileSize, keyCity, allUnits, visibleTiles, aiHasPerfectInfo))
        {
            return true;
        }

        if (unit == plan.AssignedScoutUnit)
        {
            if (plan.PreferredScoutPosition.HasValue)
            {
                Vector3 targetPosition = plan.PreferredScoutPosition.Value;
                if ((unit.transform.position - targetPosition).sqrMagnitude > 0.0001f)
                {
                    MoveAIUnitTowardEmptyTile(unit, targetPosition, tileSize, keyCity, allUnits, visibleTiles, aiHasPerfectInfo);
                }

                return true;
            }

            return false;
        }

        return false;
    }

    private bool TryExecuteAssignedCombatDefense(
        Unit unit,
        Unit assignedCombatUnit,
        Vector3? preferredCombatPosition,
        AITurnLogic.CityDefensePlan plan,
        bool isOnProtectedCity,
        float tileSize,
        City keyCity,
        Unit[] allUnits,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        if (unit != assignedCombatUnit)
        {
            return false;
        }

        if (isOnProtectedCity && !plan.CanVacateCityTile)
        {
            return true;
        }

        if (preferredCombatPosition.HasValue)
        {
            Vector3 targetPosition = preferredCombatPosition.Value;
            if ((unit.transform.position - targetPosition).sqrMagnitude > 0.0001f)
            {
                if (isOnProtectedCity)
                {
                    Unit occupant = GridUtils.GetUnitAtPosition(targetPosition, unit);
                    if (occupant == null)
                    {
                        MoveAIUnitOneStep(unit, targetPosition, tileSize, aiHasPerfectInfo);
                    }
                    else if (occupant.ownerSeatIndex == unit.ownerSeatIndex)
                    {
                        MoveAIUnitTowardEmptyTile(unit, targetPosition, tileSize, keyCity, allUnits, visibleTiles, aiHasPerfectInfo);
                    }
                }
                else
                {
                    MoveAIUnitTowardEmptyTile(unit, targetPosition, tileSize, keyCity, allUnits, visibleTiles, aiHasPerfectInfo);
                }
            }

            return true;
        }

        return isOnProtectedCity;
    }

    private bool TryAttackVisibleEnemyFromCurrentPosition(Unit unit, List<Unit> visibleEnemyUnits)
    {
        if (unit == null || visibleEnemyUnits == null || !unit.CanAttackThisTurn() || gridManager == null)
        {
            return false;
        }

        Unit bestEnemy = SelectBestLocalAttackTarget(unit, visibleEnemyUnits);
        if (bestEnemy == null)
        {
            return false;
        }

        unit.RegisterAttack();
        bool killed = unit.Attack(bestEnemy);
        if (killed && unit.AdvancesIntoDefenderTileOnKill)
        {
            unit.transform.position = bestEnemy.transform.position;
            if (SoundManager.Instance != null && !ShouldSuppressAIVsAIAudio())
            {
                SoundManager.Instance.PlayMove();
            }
        }

        City city = GridUtils.GetCityAtPosition(unit.transform.position);
        if (city != null && city.ownerSeatIndex != unit.ownerSeatIndex)
        {
            OnCityCaptured(unit.ownerSeatIndex, city);
        }

        return true;
    }

    private List<Vector3> BuildUnitPositionList(List<Unit> units)
    {
        List<Vector3> positions = new List<Vector3>(units != null ? units.Count : 0);
        if (units == null)
            return positions;

        for (int i = 0; i < units.Count; i++)
        {
            Unit unit = units[i];
            if (unit != null && unit.gameObject.activeInHierarchy)
            {
                positions.Add(unit.transform.position);
            }
        }

        return positions;
    }

    private Vector3? FindNearestTargetPosition(Vector3 from, List<Vector3> targets)
    {
        if (targets == null || targets.Count == 0)
            return null;

        Vector3? bestTarget = null;
        float bestDistanceSquared = float.MaxValue;
        for (int i = 0; i < targets.Count; i++)
        {
            Vector3 target = targets[i];
            float distanceSquared = (target - from).sqrMagnitude;
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestTarget = target;
            }
        }

        return bestTarget;
    }

    private void MoveAIUnitTowardEmptyTile(
        Unit unit,
        Vector3 targetPosition,
        float tileSize,
        City keyCity,
        Unit[] allUnits,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        if (unit == null || !unit.CanMoveThisTurn() || gridManager == null)
            return;

        Vector3 from = unit.transform.position;
        Vector3 delta = targetPosition - from;
        delta.z = 0f;
        if (delta.sqrMagnitude < 0.01f)
            return;

        List<Vector3> candidatePositions = new List<Vector3>(8);
        float primaryStepX = Mathf.Abs(delta.x) > 0.1f ? Mathf.Sign(delta.x) * tileSize : 0f;
        float primaryStepY = Mathf.Abs(delta.y) > 0.1f ? Mathf.Sign(delta.y) * tileSize : 0f;
        Vector3 primaryCandidate = new Vector3(from.x + primaryStepX, from.y + primaryStepY, from.z);
        candidatePositions.Add(primaryCandidate);

        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                if (ox == 0 && oy == 0)
                    continue;

                Vector3 alternateCandidate = from + new Vector3(ox * tileSize, oy * tileSize, 0f);
                bool alreadyAdded = false;
                for (int index = 0; index < candidatePositions.Count; index++)
                {
                    if ((candidatePositions[index] - alternateCandidate).sqrMagnitude < 0.0001f)
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded)
                {
                    candidatePositions.Add(alternateCandidate);
                }
            }
        }

        List<AIPostCalculusLocalDecisionHelper.MoveCandidate> candidates =
            new List<AIPostCalculusLocalDecisionHelper.MoveCandidate>(candidatePositions.Count);
        AILocalDecisionFeatures enabledFeatures = GetAILocalDecisionFeaturesForSide(unit.isPlayerOwned);
        for (int i = 0; i < candidatePositions.Count; i++)
        {
            Vector3 candidatePosition = candidatePositions[i];
            if (!gridManager.TryGetTileAtWorldPosition(candidatePosition, out TileVisibility candidateTile) || candidateTile == null)
                continue;

            Unit occupyingUnit = GridUtils.GetUnitAtPosition(candidatePosition, unit);
            if (occupyingUnit != null)
                continue;

            bool immediatelyLosesKeyCity =
                AIPostCalculusLocalDecisionHelper.HasFeature(enabledFeatures, AILocalDecisionFeatures.DefensiveVeto) &&
                WouldMoveCandidateExposeVisibleImmediateThreatToKeyCity(
                    unit,
                    candidatePosition,
                    keyCity,
                    allUnits,
                    visibleTiles,
                    aiHasPerfectInfo);
            candidates.Add(new AIPostCalculusLocalDecisionHelper.MoveCandidate(
                candidatePosition,
                candidateTile.gridX,
                candidateTile.gridY,
                (targetPosition - candidatePosition).sqrMagnitude,
                immediatelyLosesKeyCity));
        }

        if (!AIPostCalculusLocalDecisionHelper.TryChooseMoveDestination(
                enabledFeatures,
                candidates,
                out Vector3 chosenDestination))
            return;

        if (unit.currentCity != null)
        {
            unit.currentCity.stationedUnit = null;
            unit.currentCity = null;
        }

        unit.transform.position = chosenDestination;
        unit.RegisterMove();

        if (SoundManager.Instance != null && !ShouldSuppressAIVsAIAudio())
        {
            SoundManager.Instance.PlayMove();
        }

        City city = GridUtils.GetCityAtPosition(unit.transform.position);
        if (city != null && city.ownerSeatIndex != unit.ownerSeatIndex)
        {
            OnCityCaptured(unit.ownerSeatIndex, city);
        }
    }

    void MoveAIUnitOneStep(Unit unit, Vector3 targetPosition, float tileSize, bool aiHasPerfectInfo = false)
    {
        if (!unit.CanMoveThisTurn())
            return;

        Vector3 from = unit.transform.position;
        Vector3 delta = targetPosition - from;
        delta.z = 0f;

        // If already very close to the target, no need to move
        if (delta.sqrMagnitude < 0.01f)
            return;

        // Decide a step of at most one tile in each axis (diagonal allowed)
        float stepX = 0f;
        float stepY = 0f;

        if (Mathf.Abs(delta.x) > 0.1f)
        {
            stepX = Mathf.Sign(delta.x) * tileSize;
        }
        if (Mathf.Abs(delta.y) > 0.1f)
        {
            stepY = Mathf.Sign(delta.y) * tileSize;
        }

        Vector3 move = new Vector3(stepX, stepY, 0f);
        if (move.sqrMagnitude < 0.01f)
            return;

        // If the unit was stationed in a city, clear that link when it moves away
        if (unit.currentCity != null)
        {
            unit.currentCity.stationedUnit = null;
            unit.currentCity = null;
        }

        Vector3 newPos = from + move;
        newPos.z = from.z;

        Unit targetUnit = GridUtils.GetUnitAtPosition(newPos, unit);
        if (targetUnit != null)
        {
            // Same owner: do not move onto this tile
            if (targetUnit.ownerSeatIndex == unit.ownerSeatIndex)
            {
                return;
            }

            if (!unit.CanAttackThisTurn() || unit.AttackRange > 1)
            {
                return;
            }

            // Enemy: attack
            unit.RegisterAttack();
            unit.RegisterMove();
            bool killed = unit.Attack(targetUnit);

            if (killed && unit.AdvancesIntoDefenderTileOnKill)
            {
                unit.transform.position = newPos;

                if (SoundManager.Instance != null && !ShouldSuppressAIVsAIAudio())
                {
                    SoundManager.Instance.PlayMove();
                }
            }
        }
        else
        {
            // Empty tile: move normally
            unit.transform.position = newPos;
            unit.RegisterMove();

            if (SoundManager.Instance != null && !ShouldSuppressAIVsAIAudio())
            {
                SoundManager.Instance.PlayMove();
            }
        }

        // After moving, if the AI unit has not attacked yet,
        // look for an enemy within attack range (move-then-attack).
        if (unit.CanAttackThisTurn())
        {
            IList<Unit> perceivedEnemyUnits = aiHasPerfectInfo
                ? (IList<Unit>)Object.FindObjectsByType<Unit>()
                : BuildPerceivedEnemyUnitsForSide(
                    unit.isPlayerOwned,
                    ComputeVisibilityForSide(unit.isPlayerOwned),
                    aiHasPerfectInfo: false);
            Unit bestEnemy = SelectBestLocalAttackTarget(unit, perceivedEnemyUnits);

            if (bestEnemy != null)
            {
                unit.RegisterAttack();
                unit.RegisterMove();
                bool killed = unit.Attack(bestEnemy);

                if (killed && unit.AdvancesIntoDefenderTileOnKill)
                {
                    unit.transform.position = bestEnemy.transform.position;
                }
            }
        }

        // Check for city capture after moving or killing
        City city = GridUtils.GetCityAtPosition(unit.transform.position);
        if (city != null && city.ownerSeatIndex != unit.ownerSeatIndex)
        {
            OnCityCaptured(unit.ownerSeatIndex, city);
        }
    }

    public void OnCityCaptured(int capturedBySeatIndex, City capturedCity = null)
    {
        if (gameOver)
            return;

        if (capturedCity != null && capturedCity.ownerSeatIndex != capturedBySeatIndex)
        {
            capturedCity.SetOwnerSeatIndex(capturedBySeatIndex);
            OwnedSprite cityOwnerVisual = capturedCity.GetComponent<OwnedSprite>();
            if (cityOwnerVisual != null)
            {
                cityOwnerVisual.SetOwnerSeatIndex(capturedBySeatIndex);
            }
        }

        gameOver = true;

        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.HideAllMoveOutlines();
            UnitSelectionManager.Instance.ClearSelection();
        }

        if (TileHoverManager.Instance != null)
        {
            TileHoverManager.Instance.ClearSelection();
        }

        if (CityUIManager.Instance != null)
        {
            CityUIManager.Instance.ClosePanel();
        }

        int winnerSeatIndex = capturedBySeatIndex;
        bool handledPbpGameOver = currentMode == GameMode.PlayByPost &&
                                  TryConfigurePbpEndgameForWinnerSeat(winnerSeatIndex, "capture");
        string message = currentMode == GameMode.PlayByPost
            ? null
            : capturedBySeatIndex == 0 ? "You Win!" : "You Lose!";
        if (handledPbpGameOver)
        {
            SavePlayByPostPerGameSnapshot(historySource: "capture_gameover");
        }
        else if (currentMode == GameMode.PlayByPost)
        {
            ConfigurePbpEndgameFallback("capture_result_resolution_failed");
        }

        if (SoundManager.Instance != null && !ShouldSuppressAIVsAIAudio())
        {
            // In Play-by-Post both sides are human-controlled, so always treat game-over as a "win" cue.
            bool playWinCue = (currentMode == GameMode.VsAI) ? (capturedBySeatIndex == 0) : true;
            SoundManager.Instance.PlayGameOver(playWinCue);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (TryHandleCompletedAIVsAIDebugMatch(capturedBySeatIndex == 0 ? "SideA" : "SideB"))
        {
            return;
        }
#endif

        if (currentMode != GameMode.PlayByPost && !handledPbpGameOver)
        {
            ShowGameOverPopup(message);
        }
    }

    private AIVsAIMatchCsvLogger.MatchResult BuildAIVsAIDebugMatchResult(string winner)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!IsAIVsAIDebugModeActive() || currentMode != GameMode.VsAI || gridManager == null)
        {
            return null;
        }

        CountBoardStateForSide(true, out int sideACityCount, out int sideAUnitCount);
        CountBoardStateForSide(false, out int sideBCityCount, out int sideBUnitCount);

        return new AIVsAIMatchCsvLogger.MatchResult
        {
            timestampUtc = System.DateTime.UtcNow.ToString("o"),
            appVersion = CurrentAppVersion,
            mapSizePreset = GetCurrentMapSizePreset().ToString(),
            boardWidth = gridManager.width,
            boardHeight = gridManager.height,
            gameMode = "VsAI_Dev_AIVsAI",
            sideAAIConfig = BuildAIConfigLabelForSide(true),
            sideBAIConfig = BuildAIConfigLabelForSide(false),
            sideAProfile = GetAIDebugProfileForSide(true).ToString(),
            sideBProfile = GetAIDebugProfileForSide(false).ToString(),
            winner = winner,
            totalTurnCount = turnNumber,
            sideAFinalCityCount = sideACityCount,
            sideBFinalCityCount = sideBCityCount,
            sideAFinalUnitCount = sideAUnitCount,
            sideBFinalUnitCount = sideBUnitCount
        };
#else
        return null;
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool IsAIVsAIBatchModeActive()
    {
        return IsAIVsAIDebugModeActive() && AIVsAIBatchRunController.HasActiveRun;
    }

    private bool TryHandleCompletedAIVsAIDebugMatch(string winner)
    {
        if (!IsAIVsAIDebugModeActive())
        {
            return false;
        }

        TraceBottomRightControlCheckpointForUi("MatchCompletionStart");

        AIVsAIBatchRunController.SimulationSettings completedRunSettings = default;
        bool hadCompletedRunSettings = IsAIVsAIBatchModeActive() &&
                                       AIVsAIBatchRunController.TryGetActiveSimulationSettings(out completedRunSettings);
        AIVsAIMatchCsvLogger.MatchResult matchResult = BuildAIVsAIDebugMatchResult(winner);
        if (matchResult == null)
        {
            QueueAIVsAIDebugMatchRestart();
            return true;
        }

        bool isRunComplete = false;
        AIVsAIMatchCsvLogger.RunSummary runSummary = null;
        if (IsAIVsAIBatchModeActive())
        {
            AIVsAIBatchRunController.TryRecordMatch(matchResult, out isRunComplete, out runSummary);
        }

        AIVsAIMatchCsvLogger.TryAppendResult(matchResult);

        if (!isRunComplete)
        {
            TraceBottomRightControlCheckpointForUi("MatchCompletionQueuedRestart");
            QueueAIVsAIDebugMatchRestart();
            return true;
        }

        if (runSummary != null)
        {
            AIVsAIMatchCsvLogger.TryAppendRunSummary(runSummary);
            gameOverUiRepeatAIVsAISimulationSettings = hadCompletedRunSettings
                ? AIVsAIBatchRunController.SanitizeSimulationSettings(completedRunSettings)
                : AIVsAIBatchRunController.SanitizeSimulationSettings(
                    new AIVsAIBatchRunController.SimulationSettings
                    {
                        preset = runSummary.simulationPreset,
                        evaluationMethod = runSummary.evaluationMethod,
                        certaintyThreshold = runSummary.certaintyThreshold,
                        minimumGames = runSummary.minimumGames,
                        timeBudgetSeconds = runSummary.timeBudgetSeconds,
                        batchSize = runSummary.batchSize,
                        emergencyHardMaxGames = runSummary.emergencyHardMaxGames
                    });
            gameOverUiRepeatSideARecruitVariant = runSummary.baseSideARecruitVariant;
            gameOverUiRepeatSideBRecruitVariant = runSummary.baseSideBRecruitVariant;
            gameOverUiRepeatSideAFeatures = runSummary.baseSideAFeatures;
            gameOverUiRepeatSideBFeatures = runSummary.baseSideBFeatures;
            gameOverUiRepeatSideAProfile = runSummary.baseSideAProfile;
            gameOverUiRepeatSideBProfile = runSummary.baseSideBProfile;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SetGameOverUiTitle(runSummary.runEndedNormally ? AIVsAISimulationCompleteTitle : AIVsAISimulationAbortedTitle);
            SetGameOverPrimaryCopyTextButtonState("Copy Text", true);
            SetGameOverSecondaryButtonState(
                "Back",
                visible: true,
                interactable: true,
                action: GameOverSecondaryAction.BackToAIVsAISettings);
            SetGameOverTertiaryButtonState(
                GetAIVsAIBatchCompletionContinueLabel(runSummary),
                visible: true,
                interactable: true,
                action: GameOverTertiaryAction.RestartAIVsAIBatch);
#endif
            string completionMessage = BuildAIVsAISimulationCompletionMessage(runSummary);
            bool shouldAutoRestartCompletedTournament =
                ShouldAutoRestartCompletedTournament(runSummary, gameOverUiRepeatAIVsAISimulationSettings);
            if (shouldAutoRestartCompletedTournament && aiVsAiDebugPaused)
            {
                Debug.Log(
                    $"[AIVsAIBatch] Suppressing continuous tournament auto-restart because pause was active for runId={runSummary.runId}.");
                shouldAutoRestartCompletedTournament = false;
            }

            if (shouldAutoRestartCompletedTournament)
            {
                Debug.Log(
                    $"[AIVsAIBatch] Auto-restarting tournament after completed runId={runSummary.runId} " +
                    $"matches={runSummary.matchCount}/{runSummary.tournamentScheduledGameCount} " +
                    $"participants={runSummary.tournamentParticipantCount} " +
                    $"continuous={gameOverUiRepeatAIVsAISimulationSettings.tournamentRunContinuously}");
                TraceBottomRightControlCheckpointForUi("CompletedTournamentQueuedAutoRestart");
                QueueCompletedTournamentAutoRestart(completionMessage);
                return true;
            }

            ShowGameOverPopup(completionMessage);
            if (runSummary.simulationMode == AIVsAIBatchRunController.SimulationMode.Tournament)
            {
                Debug.Log(
                    $"[AIVsAIBatch] Finished tournament runId={runSummary.runId} status={(runSummary.runEndedNormally ? "Normal" : "Abnormal")} " +
                    $"stopReason={runSummary.stopReason} matches={runSummary.matchCount}/{runSummary.tournamentScheduledGameCount} " +
                    $"participants={runSummary.tournamentParticipantCount} pairings={runSummary.tournamentScheduledPairingCount} " +
                    $"winner={runSummary.tournamentWinnerLabel} elapsed={runSummary.elapsedSeconds:0.00}s " +
                    $"matchCsv={AIVsAIMatchCsvLogger.GetResultsFilePath()} summaryCsv={AIVsAIMatchCsvLogger.GetRunSummaryFilePath()}");
            }
            else
            {
                Debug.Log(
                    $"[AIVsAIBatch] Finished runId={runSummary.runId} status={(runSummary.runEndedNormally ? "Normal" : "Abnormal")} " +
                    $"stopReason={runSummary.stopReason} matches={runSummary.matchCount} " +
                    $"sideAWins={runSummary.sideAWins} sideBWins={runSummary.sideBWins} draws={runSummary.trueDraws} aborts={runSummary.aborts} " +
                    $"scoreRate={runSummary.sideAScoreRate:P1} effect={runSummary.sideAEffectSize:+0.0%;-0.0%;0.0%} " +
                    $"bayesProb={runSummary.bayesianSideABetterProbability:P1} decisiveGames={runSummary.bayesianDecisiveGames} " +
                    $"elapsed={runSummary.elapsedSeconds:0.00}s batchSize={runSummary.batchSize} " +
                    $"matchCsv={AIVsAIMatchCsvLogger.GetResultsFilePath()} summaryCsv={AIVsAIMatchCsvLogger.GetRunSummaryFilePath()}");
            }
            return true;
        }

        QueueAIVsAIDebugMatchRestart();
        return true;
    }

    private void QueueCompletedTournamentAutoRestart(string completionMessage)
    {
        aiVsAiCompletedTournamentAutoRestartPending = true;
        aiVsAiCompletedTournamentAutoRestartMessage = completionMessage ?? string.Empty;
        QueueAIVsAIDebugMatchRestart();
    }

    private string GetAIVsAIBatchCompletionContinueLabel(AIVsAIMatchCsvLogger.RunSummary runSummary)
    {
        if (runSummary != null &&
            runSummary.simulationMode == AIVsAIBatchRunController.SimulationMode.Tournament &&
            runSummary.runEndedNormally)
        {
            return gameOverUiRepeatAIVsAISimulationSettings.mode == AIVsAIBatchRunController.SimulationMode.Tournament &&
                   gameOverUiRepeatAIVsAISimulationSettings.tournamentRunContinuously
                ? "Continue Tournament"
                : "Run Again";
        }

        return "Run Again";
    }

    private static bool ShouldAutoRestartCompletedTournament(
        AIVsAIMatchCsvLogger.RunSummary runSummary,
        AIVsAIBatchRunController.SimulationSettings simulationSettings)
    {
        return runSummary != null &&
               runSummary.runEndedNormally &&
               runSummary.simulationMode == AIVsAIBatchRunController.SimulationMode.Tournament &&
               string.Equals(
                   runSummary.stopReason,
                   AIVsAIBatchRunController.StopReason.TournamentScheduleCompleted.ToString(),
                   System.StringComparison.Ordinal) &&
               simulationSettings.mode == AIVsAIBatchRunController.SimulationMode.Tournament &&
               simulationSettings.tournamentRunContinuously;
    }

    private bool TryHandleAbortedAIVsAIDebugMatch(string abortReason)
    {
        if (!IsAIVsAIBatchModeActive())
        {
            return false;
        }

        gameOver = true;
        Debug.LogWarning($"[AIVsAIBatch] Match aborted ({abortReason}).");
        return TryHandleCompletedAIVsAIDebugMatch(AIVsAIDebugAbortWinner);
    }

    private static string FormatDuration(float totalSeconds)
    {
        System.TimeSpan duration = System.TimeSpan.FromSeconds(Mathf.Max(0f, totalSeconds));
        if (duration.TotalHours >= 1d)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        }

        if (duration.TotalMinutes >= 1d)
        {
            return $"{duration.Minutes}m {duration.Seconds}s";
        }

        return $"{duration.TotalSeconds:0.0}s";
    }

    private static string BuildPrimaryAIVsAIDisplayLabel(string aiConfig, string fallbackLabel)
    {
        string profile = ExtractAIVsAiConfigValue(aiConfig, "profile");
        string recruitVariant = ExtractAIVsAiConfigValue(aiConfig, "recruitVariant");
        string localFeatures = ExtractAIVsAiConfigValue(aiConfig, "localFeatures");

        string modelLabel = BuildAIVariantModelLabel(profile, recruitVariant, fallbackLabel);
        string featureLabel = BuildAIVariantFeatureLabel(localFeatures);
        return $"{modelLabel} [{featureLabel}]";
    }

    private static string BuildAIVariantModelLabel(string profile, string recruitVariant, string fallbackLabel)
    {
        if (string.Equals(recruitVariant, AIRecruitVariant.RiderFocus.ToString(), System.StringComparison.Ordinal))
        {
            return "Rider Focus";
        }

        if (!string.IsNullOrWhiteSpace(profile))
        {
            return profile;
        }

        if (string.Equals(recruitVariant, AIRecruitVariant.Default.ToString(), System.StringComparison.Ordinal))
        {
            return "Baseline";
        }

        return fallbackLabel;
    }

    private static string BuildAIVariantFeatureLabel(string localFeatures)
    {
        if (string.IsNullOrWhiteSpace(localFeatures) ||
            string.Equals(localFeatures, "none", System.StringComparison.OrdinalIgnoreCase))
        {
            return "None";
        }

        string[] tokens = localFeatures.Split('+');
        List<string> labels = new List<string>(tokens.Length);
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i].Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            switch (token)
            {
                case "offense":
                    labels.Add("Offense");
                    break;

                case "exchange":
                    labels.Add("Exchange");
                    break;

                case "defense":
                    labels.Add("Defense");
                    break;

                default:
                    labels.Add(token);
                    break;
            }
        }

        return labels.Count > 0
            ? string.Join(" + ", labels)
            : "None";
    }

    private static string BuildSecondaryAIVsAISideLabel(string primaryLabel, string sideName)
    {
        return $"{primaryLabel} ({sideName})";
    }

    private static string ExtractAIVsAiConfigValue(string aiConfig, string key)
    {
        if (string.IsNullOrWhiteSpace(aiConfig) || string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        string[] segments = aiConfig.Split(';');
        string expectedPrefix = $"{key}=";
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            if (segment.StartsWith(expectedPrefix, System.StringComparison.Ordinal))
            {
                return segment.Substring(expectedPrefix.Length).Trim();
            }
        }

        return string.Empty;
    }

    private static string BuildAIVsAISimulationCompletionMessage(AIVsAIMatchCsvLogger.RunSummary summary)
    {
        if (summary == null)
        {
            return "AI simulation finished.";
        }

        if (summary.simulationMode == AIVsAIBatchRunController.SimulationMode.Tournament)
        {
            string tournamentStatusText = summary.runEndedNormally
                ? "Normal"
                : "Abnormal (result may be unreliable)";
            string standings = summary.tournamentStandingsSummary ?? string.Empty;
            string[] standingLines = standings.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            string topStandings = standingLines.Length > 0
                ? string.Join("\n", standingLines, 0, Mathf.Min(5, standingLines.Length))
                : "No completed standings.";
            string pairings = summary.tournamentPairingSummary ?? string.Empty;
            string[] pairingLines = pairings.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            string topPairings = pairingLines.Length > 0
                ? string.Join("\n", pairingLines, 0, Mathf.Min(3, pairingLines.Length))
                : "No completed pairings.";
            string winnerLabel = string.IsNullOrWhiteSpace(summary.tournamentWinnerLabel)
                ? "No tournament leader"
                : summary.tournamentWinnerLabel;

            return
                $"Tournament winner: {winnerLabel}\n" +
                $"Run status: {tournamentStatusText}\n" +
                $"Stop reason: {summary.stopReason}\n" +
                $"Mode: {AIVsAIBatchRunController.GetSimulationModeDisplayName(summary.simulationMode)}\n" +
                $"Format: {AIVsAIBatchRunController.GetTournamentTypeDisplayName(summary.tournamentType)}\n" +
                $"Participants: {summary.tournamentParticipantCount}\n" +
                $"Pairings: {summary.tournamentScheduledPairingCount}\n" +
                $"Completed games: {summary.matchCount}/{summary.tournamentScheduledGameCount}\n" +
                $"Games per pairing: {summary.tournamentGamesPerPairing}\n" +
                $"Seat swap: {(summary.tournamentSeatSwapEnabled ? "On" : "Off")}\n" +
                $"Draws: {summary.trueDraws}\n" +
                $"Aborts: {summary.aborts}\n" +
                $"Elapsed time: {FormatDuration(summary.elapsedSeconds)}\n" +
                $"Average turns: {summary.averageTotalTurnCount:0.00}\n" +
                $"Turns/sec: {summary.turnsPerSecond:0.00}\n" +
                $"Top standings:\n{topStandings}\n" +
                $"Pairing samples:\n{topPairings}";
        }

        string primarySideALabel = BuildPrimaryAIVsAIDisplayLabel(summary.sideAAIConfig, "Side A");
        string primarySideBLabel = BuildPrimaryAIVsAIDisplayLabel(summary.sideBAIConfig, "Side B");
        string secondarySideALabel = BuildSecondaryAIVsAISideLabel(primarySideALabel, "Side A");
        string secondarySideBLabel = BuildSecondaryAIVsAISideLabel(primarySideBLabel, "Side B");
        bool variantsAppearIdentical =
            string.Equals(summary.sideAAIConfig, summary.sideBAIConfig, System.StringComparison.Ordinal) ||
            string.Equals(primarySideALabel, primarySideBLabel, System.StringComparison.Ordinal);
        float favoredProbability = summary.bayesianSideABetterProbability >= 0.5f
            ? summary.bayesianSideABetterProbability
            : 1f - summary.bayesianSideABetterProbability;
        float severityPoints = Mathf.Abs(summary.sideAEffectSize) * 100f;
        string severityText = $"{severityPoints:0.0} pts";
        string headline;
        string conclusion;
        if (variantsAppearIdentical)
        {
            headline = $"Current result: no variant difference established yet ({favoredProbability:P1}, {severityText})";
            conclusion = "No variant difference established yet";
        }
        else if (summary.bayesianSideABetterProbability >= summary.certaintyThreshold)
        {
            headline = $"Result: {primarySideALabel} is likely stronger than {primarySideBLabel} ({summary.bayesianSideABetterProbability:P1}, +{severityPoints:0.0} pts)";
            conclusion = $"{primarySideALabel} is likely stronger than {primarySideBLabel}";
        }
        else if (summary.bayesianSideABetterProbability <= (1f - summary.certaintyThreshold))
        {
            headline = $"Result: {primarySideBLabel} is likely stronger than {primarySideALabel} ({(1f - summary.bayesianSideABetterProbability):P1}, +{severityPoints:0.0} pts)";
            conclusion = $"{primarySideBLabel} is likely stronger than {primarySideALabel}";
        }
        else
        {
            bool currentlyFavorSideA = summary.bayesianSideABetterProbability > 0.5f;
            bool effectivelyTied = Mathf.Abs(summary.sideAEffectSize) < 0.0005f;
            if (effectivelyTied)
            {
                headline = $"Current result: no variant clearly favored yet ({favoredProbability:P1}, {severityText})";
            }
            else
            {
                string favoredLabel = currentlyFavorSideA ? primarySideALabel : primarySideBLabel;
                string otherLabel = currentlyFavorSideA ? primarySideBLabel : primarySideALabel;
                headline = $"Current result: {favoredLabel} currently favored over {otherLabel} ({favoredProbability:P1}, +{severityPoints:0.0} pts)";
            }
            conclusion = $"No clear winner yet between {primarySideALabel} and {primarySideBLabel}";
        }

        string statusText = summary.runEndedNormally
            ? "Normal"
            : "Abnormal (result may be unreliable)";
        string pairSummaryText = summary.completePairCount > 0
            ? $"Completed swap pairs: {summary.completePairCount}\n" +
              $"Pair record: {primarySideALabel}-favored {summary.pairedAFavoredCount} | Split {summary.pairedSplitCount} | {primarySideBLabel}-favored {summary.pairedBFavoredCount}"
            : "Completed swap pairs: 0\nPair record: no completed swap pairs yet";
        string message =
            $"{headline}\n" +
            $"Run status: {statusText}\n" +
            $"Stop reason: {summary.stopReason}\n" +
            $"Preset: {summary.simulationSettingsLabel}\n" +
            $"Method: {summary.evaluationMethodLabel}\n" +
            $"Completed games: {summary.matchCount}\n" +
            $"{primarySideALabel} wins: {summary.sideAWins}\n" +
            $"{primarySideBLabel} wins: {summary.sideBWins}\n" +
            $"Draws: {summary.trueDraws}\n" +
            $"Aborts: {summary.aborts}\n" +
            $"Estimated win rate ({primarySideALabel}): {summary.sideAScoreRate:P1}\n" +
            $"Effect size: {summary.sideAEffectSize:+0.0%;-0.0%;0.0%}\n" +
            $"Bayesian P({primarySideALabel} > {primarySideBLabel}): {summary.bayesianSideABetterProbability:P1}\n" +
            $"Conclusion: {conclusion}\n" +
            $"Decisive games used for certainty: {summary.bayesianDecisiveGames}\n" +
            $"Certainty threshold: {summary.certaintyThreshold:P0}\n" +
            $"Minimum games: {summary.minimumGames}\n" +
            $"Batch size: {summary.batchSize}\n" +
            $"Elapsed time: {FormatDuration(summary.elapsedSeconds)}\n" +
            $"Time budget: {FormatDuration(summary.timeBudgetSeconds)}\n" +
            $"Emergency hard max games: {summary.emergencyHardMaxGames}\n" +
            $"{pairSummaryText}\n" +
            $"Average turns: {summary.averageTotalTurnCount:0.00}\n" +
            $"Turns/sec: {summary.turnsPerSecond:0.00}\n" +
            $"{secondarySideALabel}\n" +
            $"{secondarySideBLabel}";

        if (summary.drawsOrAborts > 0)
        {
            message = $"{message}\nBayesian certainty excludes draws and aborts.";
        }

        return message;
    }
#endif

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
    private bool TryHandleAbortedAIVsAIDebugMatch(string abortReason)
    {
        return false;
    }
#endif

    private void CountBoardStateForSide(bool sideIsPlayerOwned, out int cityCount, out int unitCount)
    {
        cityCount = 0;
        unitCount = 0;

        City[] cities = Object.FindObjectsByType<City>();
        for (int i = 0; i < cities.Length; i++)
        {
            City city = cities[i];
            if (city != null && city.isPlayerOwned == sideIsPlayerOwned)
            {
                cityCount++;
            }
        }

        Unit[] units = Object.FindObjectsByType<Unit>();
        for (int i = 0; i < units.Length; i++)
        {
            Unit unit = units[i];
            if (unit != null && unit.isPlayerOwned == sideIsPlayerOwned)
            {
                unitCount++;
            }
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void QueueAIVsAIDebugMatchRestart()
    {
        if (!IsAIVsAIDebugModeActive() || aiVsAiDebugRestartPending)
        {
            return;
        }

        aiVsAiDebugRestartPending = true;
        TraceBottomRightControlCheckpointForUi("RestartQueued");
        RefreshEndTurnButtonInteractable(force: true);
        StartCoroutine(RestartAIVsAIDebugMatchAfterDelay());
    }

    private IEnumerator RestartAIVsAIDebugMatchAfterDelay()
    {
        yield return new WaitForSecondsRealtime(GetAIVsAIDebugRestartDelaySeconds());

        if (!aiVsAiDebugRestartPending)
        {
            yield break;
        }

        if (currentMode != GameMode.VsAI || !enableAIVsAIDebugMode)
        {
            aiVsAiDebugRestartPending = false;
            ClearAIVsAICompletedTournamentAutoRestartState();
            yield break;
        }

        SaveLoadRequest.ClearPending();
        GameModeSelection.SetPendingMode(GameMode.VsAI);
        MapSizeSelection.SetPending(GetCurrentMapSizePreset());
        AIRecruitVariantSelection.SetPending(aiRecruitVariant);
        SnapshotHistorySelection.SetPending(MatchSnapshotHistorySettings.IsEnabled(currentGameId));
        AIRecruitVariant nextSideARecruitVariant = aiVsAiSideARecruitVariant;
        AIRecruitVariant nextSideBRecruitVariant = aiVsAiSideBRecruitVariant;
        AILocalDecisionFeatures nextSideAFeatures = aiVsAiSideAFeatures;
        AILocalDecisionFeatures nextSideBFeatures = aiVsAiSideBFeatures;
        AIDebugProfile nextSideAProfile = aiVsAiSideAProfile;
        AIDebugProfile nextSideBProfile = aiVsAiSideBProfile;
        if (IsAIVsAIBatchModeActive())
        {
            AIVsAIBatchRunController.TryGetUpcomingMatchSettings(
                out nextSideARecruitVariant,
                out nextSideBRecruitVariant,
                out nextSideAFeatures,
                out nextSideBFeatures,
                out nextSideAProfile,
                out nextSideBProfile);
        }

        AIVsAIBatchRunController.SimulationSettings simulationSettingsForRestart = gameOverUiRepeatAIVsAISimulationSettings;
        if (AIVsAIBatchRunController.TryGetActiveSimulationSettings(out AIVsAIBatchRunController.SimulationSettings activeSimulationSettings))
        {
            simulationSettingsForRestart = activeSimulationSettings;
        }

        QueuePendingAIVsAIDebugSelectionForSimulation(
            simulationSettingsForRestart,
            nextSideARecruitVariant,
            nextSideBRecruitVariant,
            nextSideAFeatures,
            nextSideBFeatures,
            nextSideAProfile,
            nextSideBProfile);

        TraceBottomRightControlCheckpointForUi("RestartCoroutineBeforeSceneLoad");
        ClearAIVsAICompletedTournamentAutoRestartState();
        Time.timeScale = 1f;
        CameraController.ClearPendingRestoreState();
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }

    private void QueuePendingAIVsAIDebugSelectionForSimulation(
        AIVsAIBatchRunController.SimulationSettings simulationSettings,
        AIRecruitVariant fallbackSideARecruitVariant,
        AIRecruitVariant fallbackSideBRecruitVariant,
        AILocalDecisionFeatures fallbackSideAFeatures,
        AILocalDecisionFeatures fallbackSideBFeatures,
        AIDebugProfile fallbackSideAProfile,
        AIDebugProfile fallbackSideBProfile)
    {
        AIRecruitVariant pendingSideARecruitVariant = fallbackSideARecruitVariant;
        AIRecruitVariant pendingSideBRecruitVariant = fallbackSideBRecruitVariant;
        AILocalDecisionFeatures pendingSideAFeatures = fallbackSideAFeatures;
        AILocalDecisionFeatures pendingSideBFeatures = fallbackSideBFeatures;
        AIDebugProfile pendingSideAProfile = fallbackSideAProfile;
        AIDebugProfile pendingSideBProfile = fallbackSideBProfile;

        AIVsAIBatchRunController.SimulationSettings sanitizedSettings =
            AIVsAIBatchRunController.SanitizeSimulationSettings(simulationSettings);
        AIVsAIBatchRunController.SetPendingSimulationSettings(sanitizedSettings);
        if (sanitizedSettings.mode == AIVsAIBatchRunController.SimulationMode.Tournament)
        {
            AIVsAIBatchRunController.TryGetInitialTournamentMatchSettings(
                sanitizedSettings,
                out pendingSideARecruitVariant,
                out pendingSideBRecruitVariant,
                out pendingSideAFeatures,
                out pendingSideBFeatures,
                out pendingSideAProfile,
                out pendingSideBProfile);
        }

        AIVsAIDebugSelection.SetPending(
            enabled: true,
            sideARecruitVariant: pendingSideARecruitVariant,
            sideBRecruitVariant: pendingSideBRecruitVariant,
            sideAFeatures: pendingSideAFeatures,
            sideBFeatures: pendingSideBFeatures,
            sideAProfile: pendingSideAProfile,
            sideBProfile: pendingSideBProfile,
            batchSpeedPreset: aiVsAiBatchSpeedPreset);
    }
#endif

    void CollectPlayerIncome()
    {
        if (gameOver) return;

        int income = 0;
        City[] cities = Object.FindObjectsByType<City>();
        foreach (City city in cities)
        {
            if (city.isPlayerOwned)
            {
                income += goldPerCity;
            }
        }

        if (income > 0)
        {
            AddGold(true, income);
        }
    }

    void CollectAIGold()
    {
        if (gameOver) return;

        int baseIncome = 0;
        City[] cities = Object.FindObjectsByType<City>();
        foreach (City city in cities)
        {
            if (!city.isPlayerOwned)
            {
                baseIncome += goldPerCity;
            }
        }

        int income = ResolveAIGoldIncome(baseIncome, turnNumber);
        if (income > 0)
        {
            AddGold(false, income);
        }
    }

    /// <summary>
    /// Computes which tiles are currently visible for a given side
    /// based on cities and units that side owns, using the same
    /// radius rules as the fog-of-war visuals.
    /// This does not mutate any TileVisibility state.
    /// </summary>
    HashSet<TileVisibility> ComputeVisibilityForSeat(int ownerSeatIndex)
    {
        HashSet<TileVisibility> visibleTiles = new HashSet<TileVisibility>();

        if (gridManager == null)
            return visibleTiles;

        // Reveal around cities owned by this side
        City[] cities = Object.FindObjectsByType<City>();
        foreach (City city in cities)
        {
            if (city.ownerSeatIndex != ownerSeatIndex)
                continue;

            for (int dx = -visibilityRadius; dx <= visibilityRadius; dx++)
            {
                for (int dy = -visibilityRadius; dy <= visibilityRadius; dy++)
                {
                    int tx = city.x + dx;
                    int ty = city.y + dy;
                    if (gridManager.TryGetTile(tx, ty, out TileVisibility tile))
                    {
                        visibleTiles.Add(tile);
                    }
                }
            }
        }

        // Reveal around units owned by this side
        Unit[] units = Object.FindObjectsByType<Unit>();
        foreach (Unit unit in units)
        {
            if (unit.ownerSeatIndex != ownerSeatIndex)
                continue;

            if (!gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility originTile))
                continue;

            int unitVisionRange = Mathf.Max(1, unit.VisionRange);
            for (int dx = -unitVisionRange; dx <= unitVisionRange; dx++)
            {
                for (int dy = -unitVisionRange; dy <= unitVisionRange; dy++)
                {
                    int tx = originTile.gridX + dx;
                    int ty = originTile.gridY + dy;
                    if (gridManager.TryGetTile(tx, ty, out TileVisibility tile))
                    {
                        visibleTiles.Add(tile);
                    }
                }
            }
        }

        return visibleTiles;
    }

    HashSet<TileVisibility> ComputeVisibilityForSide(bool sideIsPlayerOwned)
    {
        return ComputeVisibilityForSeat(sideIsPlayerOwned ? 0 : 1);
    }

    public void RecalculatePlayerVisibility()
    {
        if (gridManager == null)
            return;

        int viewerSeatIndex = GetViewerSeatIndexForRuntime();

        // Reset current visibility for this side (keep per-side explored memory)
        foreach (TileVisibility tile in gridManager.GetAllTiles())
        {
            tile.SetVisibleForSeat(false, viewerSeatIndex);
        }

        // Compute which tiles should be visible for this side
        HashSet<TileVisibility> visibleTiles = ComputeVisibilityForSeat(viewerSeatIndex);
        foreach (TileVisibility tile in visibleTiles)
        {
            tile.SetVisibleForSeat(true, viewerSeatIndex);
        }

        // Hide enemy units that are not in visible tiles
        Unit[] units = Object.FindObjectsByType<Unit>();
        foreach (Unit unit in units)
        {
            bool isCurrentSideUnit = unit.ownerSeatIndex == viewerSeatIndex;
            bool isVisible = isCurrentSideUnit;
            if (!isVisible)
            {
                if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile))
                {
                    isVisible = tile.isVisibleNow;
                }
            }
            unit.SetFogVisibility(isVisible, isCurrentSideUnit);
        }
    }

    string GetDefaultSavePath()
    {
        return Path.Combine(GetPersistentRootPath(), autoSaveFileName);
    }

    private string GetPrimaryAutosavePathForCurrentMode()
    {
        // Primary autosave only: keep SP and PBp isolated from each other.
        if (currentMode == GameMode.VsAI)
        {
            return Path.Combine(GetPersistentRootPath(), SinglePlayerPrimarySaveFileName);
        }

        if (currentMode == GameMode.PlayByPost)
        {
            return Path.Combine(GetPersistentRootPath(), PlayByPostPrimarySaveFileName);
        }

        return GetDefaultSavePath();
    }

    private string GetPbpGameIdFromPrefsOrCurrent()
    {
        if (!string.IsNullOrWhiteSpace(currentGameId))
            return currentGameId;

        string prefsId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        return string.IsNullOrWhiteSpace(prefsId) ? null : prefsId;
    }

    private void PersistCurrentPbpGameIdIfNeeded()
    {
        if (currentMode != GameMode.PlayByPost)
            return;

        if (string.IsNullOrWhiteSpace(currentGameId))
            return;

        string existing = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        if (string.Equals(existing, currentGameId, System.StringComparison.Ordinal))
            return;

        PlayerPrefs.SetString(PlayByPostGameIdKey, currentGameId);
        PlayerPrefs.Save();
    }

    private string GetPbpPerGameSavePath(string gameId)
    {
        return GetPbpPerGameSavePathStatic(gameId);
    }

    private static string GetPbpPerGameSavePathStatic(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        string safeGameId = SanitizeGameIdForFileName(gameId);
        string directory = Path.Combine(GetPersistentRootPath(), PlayByPostPerGameSaveFolderName);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{PlayByPostPerGameSavePrefix}{safeGameId}.json");
    }

    private static string GetPersistentRootPath()
    {
        return DevClientInstanceScope.GetScopedPersistentDataPath();
    }

    private static string SanitizeGameIdForFileName(string gameId)
    {
        if (string.IsNullOrEmpty(gameId))
            return string.Empty;

        char[] chars = gameId.ToCharArray();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++)
        {
            if (System.Array.IndexOf(invalidChars, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private void SavePlayByPostPerGameSnapshot(string historySource = null)
    {
        if (currentMode != GameMode.PlayByPost)
            return;

        string gameId = GetPbpGameIdFromPrefsOrCurrent();
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        if (!string.Equals(currentGameId, gameId, System.StringComparison.Ordinal))
        {
            SetCurrentGameId(gameId);
        }

        string snapshotPath = GetPbpPerGameSavePath(gameId);
        if (string.IsNullOrWhiteSpace(snapshotPath))
            return;

        WritePlayByPostSnapshotFile(snapshotPath, historySource: historySource);
    }

    private void SavePlayByPostPerGameSnapshot(
        string snapshotJson,
        int snapshotRoundTurn,
        bool snapshotIsPlayerTurn,
        string snapshotGameId,
        string historySource = null)
    {
        if (currentMode != GameMode.PlayByPost)
            return;

        string gameId = string.IsNullOrWhiteSpace(snapshotGameId)
            ? GetPbpGameIdFromPrefsOrCurrent()
            : snapshotGameId;
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        if (!string.Equals(currentGameId, gameId, System.StringComparison.Ordinal))
        {
            SetCurrentGameId(gameId);
        }

        string snapshotPath = GetPbpPerGameSavePath(gameId);
        if (string.IsNullOrWhiteSpace(snapshotPath))
            return;

        WritePlayByPostSnapshotFile(
            snapshotPath,
            snapshotJson,
            gameId,
            snapshotRoundTurn,
            snapshotIsPlayerTurn,
            historySource);
    }

    private void WritePlayByPostSnapshotFile(string path, string historySource = null)
    {
        WritePlayByPostSnapshotFile(path, null, currentGameId, turnNumber, isPlayerTurn, historySource);
    }

    private void WritePlayByPostSnapshotFile(
        string path,
        string snapshotJson,
        string gameIdForLog,
        int snapshotRoundTurn,
        bool snapshotIsPlayerTurn,
        string historySource = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning($"Cannot save PBp snapshot: invalid path '{path}'.");
#endif
            return;
        }

        if (gridManager == null)
        {
            Debug.LogWarning("Cannot save PBp snapshot: gridManager is null.");
            return;
        }

        string json = snapshotJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            if (!TryBuildSaveJsonForDisk(out json))
                return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, json);
            TryCaptureSnapshotHistoryCopy(
                gameIdForLog,
                json,
                snapshotRoundTurn,
                snapshotIsPlayerTurn,
                path,
                string.IsNullOrWhiteSpace(historySource) ? "snapshot_write" : historySource);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            int snapshotTransportSeq = ComputeTransportSeq(snapshotRoundTurn, snapshotIsPlayerTurn);
            if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
            {
                Debug.Log(
                    $"PBp snapshot write gameId={gameIdForLog} path={path} roundTurn={snapshotRoundTurn} isPlayerTurn={snapshotIsPlayerTurn} transportSeq={snapshotTransportSeq}");
            }
#endif
            LogPlayByPostTelemetry(
                "SnapshotSave",
                $"path={path} state={GetPlayByPostStateSummary()}");
        }
        catch (IOException ex)
        {
            LogPlayByPostTelemetry(
                "SnapshotSaveFailed",
                $"path={path} err={ex.Message} state={GetPlayByPostStateSummary()}");
            Debug.LogError("Failed to save PBp snapshot: " + ex.Message);
        }
    }

    private string NormalizeSavePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return GetDefaultSavePath();
        }

        string normalizedPath = path;
        string normalizedDataPath = Application.dataPath;

        try
        {
            normalizedPath = Path.GetFullPath(path);
        }
        catch
        {
            // If path normalization fails, fall back to the provided path.
        }

        try
        {
            normalizedDataPath = Path.GetFullPath(Application.dataPath);
        }
        catch
        {
            // Best-effort: keep the raw dataPath.
        }

        if (!string.IsNullOrEmpty(normalizedDataPath) &&
            normalizedPath.StartsWith(normalizedDataPath, System.StringComparison.Ordinal))
        {
            Debug.LogWarning($"Save path points inside Application.dataPath; redirecting to persistentDataPath. path={normalizedPath}");
            return GetDefaultSavePath();
        }

        return path;
    }

    public void AutoSaveIfEnabled()
    {
        if (!autoSaveEnabled || isLoadingFromSave)
            return;

        SaveToFile(GetPrimaryAutosavePathForCurrentMode());
    }

    public void SaveToFile(string path = null)
    {
        string targetPath = path;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            targetPath = GetDefaultSavePath();
        }
        targetPath = NormalizeSavePath(targetPath);
        bool shouldLogPlayByPostSnapshotSave =
            currentMode == GameMode.PlayByPost && isPlayByPostWaitingForExport;

        if (gridManager == null)
        {
            Debug.LogWarning("Cannot save: gridManager is null.");
            return;
        }

        if (!TryBuildSaveJsonForDisk(out string json))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            File.WriteAllText(targetPath, json);
            TryCaptureSnapshotHistoryCopy(
                currentGameId,
                json,
                turnNumber,
                isPlayerTurn,
                targetPath,
                "save_write");
            if (shouldLogPlayByPostSnapshotSave)
            {
                LogPlayByPostTelemetry(
                    "SnapshotSave",
                    $"path={targetPath} state={GetPlayByPostStateSummary()}");
            }
            if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
            {
                Debug.Log("Game saved to " + targetPath);
            }
            SaveManifestService.RecordLocalSave(
                currentGameId,
                currentMode,
                targetPath,
                gameOver,
                lastKnownRoundTurn: turnNumber,
                lastKnownIsPlayerTurn: isPlayerTurn,
                lastKnownCurrentTurnSeatIndex: GetAuthoritativeCurrentTurnSeatIndex(),
                lastKnownTransportSeq: ComputeTransportSeq(turnNumber, GetAuthoritativeCurrentTurnSeatIndex(), GetRuntimeSeatCount()),
                lastKnownSeatCount: GetConfiguredPlayByPostSeatCount());
        }
        catch (IOException ex)
        {
            if (shouldLogPlayByPostSnapshotSave)
            {
                LogPlayByPostTelemetry(
                    "SnapshotSaveFailed",
                    $"path={targetPath} err={ex.Message} state={GetPlayByPostStateSummary()}");
            }
            Debug.LogError("Failed to save game: " + ex.Message);
        }
    }

    private void TryCaptureSnapshotHistoryCopy(
        string gameId,
        string json,
        int snapshotRoundTurn,
        bool snapshotIsPlayerTurn,
        string canonicalPath,
        string historySource,
        MatchSnapshotHistoryStore.SnapshotHistoryDecisionContext decisionContext = null)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        if (currentMode == GameMode.VsAI &&
            !string.Equals(historySource, "ai_decision_state", System.StringComparison.Ordinal))
        {
            return;
        }

        MatchSnapshotHistoryStore.TryCaptureSnapshot(
            gameId,
            currentMode,
            historySource,
            json,
            snapshotRoundTurn,
            snapshotIsPlayerTurn,
            canonicalPath,
            decisionContext);
    }

    private void TryCaptureAIDecisionSnapshot(bool actingSideIsPlayerOwned)
    {
        if (currentMode != GameMode.VsAI || gridManager == null)
        {
            return;
        }

        if (ShouldSkipAIVsAISnapshotHistory())
        {
            return;
        }

        if (!TryBuildSaveJsonForDisk(out string json) || string.IsNullOrWhiteSpace(currentGameId))
        {
            return;
        }

        HashSet<TileVisibility> visibleTiles = ComputeVisibilityForSide(actingSideIsPlayerOwned);
        MatchSnapshotHistoryStore.SnapshotHistoryDecisionContext decisionContext =
            BuildSnapshotHistoryDecisionContext(actingSideIsPlayerOwned, visibleTiles);

        TryCaptureSnapshotHistoryCopy(
            currentGameId,
            json,
            turnNumber,
            isPlayerTurn,
            canonicalPath: null,
            historySource: "ai_decision_state",
            decisionContext: decisionContext);
    }

    private MatchSnapshotHistoryStore.SnapshotHistoryDecisionContext BuildSnapshotHistoryDecisionContext(
        bool actingSideIsPlayerOwned,
        HashSet<TileVisibility> visibleTiles)
    {
        HashSet<TileVisibility> safeVisibleTiles = visibleTiles ?? new HashSet<TileVisibility>();
        List<MatchSnapshotHistoryStore.SnapshotHistoryGridCoord> visibleTileCoords =
            BuildSnapshotHistoryVisibleTileCoords(safeVisibleTiles);
        List<MatchSnapshotHistoryStore.SnapshotHistoryVisibleUnitSummary> visibleEnemyUnits =
            BuildSnapshotHistoryVisibleEnemyUnits(actingSideIsPlayerOwned, safeVisibleTiles);

        return new MatchSnapshotHistoryStore.SnapshotHistoryDecisionContext
        {
            actingSideIsPlayerOwned = actingSideIsPlayerOwned,
            viewerIsPlayerOwned = GetViewerIsPlayerOwned(),
            aiProfile = GetAIDebugProfileForSide(actingSideIsPlayerOwned).ToString(),
            visibleTileCount = safeVisibleTiles.Count,
            visibleTiles = visibleTileCoords,
            visibleEnemyUnits = visibleEnemyUnits,
            threatenedFriendlyCities = BuildSnapshotHistoryCityThreats(actingSideIsPlayerOwned, visibleEnemyUnits),
            aiReasoning = null
        };
    }

    private City FindPrimaryControlledCity(City[] allCities, bool actingSideIsPlayerOwned)
    {
        if (allCities == null)
        {
            return null;
        }

        for (int i = 0; i < allCities.Length; i++)
        {
            City city = allCities[i];
            if (city != null && city.isPlayerOwned == actingSideIsPlayerOwned)
            {
                return city;
            }
        }

        return null;
    }


    private void AddDefenseAssignmentsForPlan(
        AITurnLogic.CityDefensePlan plan,
        Dictionary<Unit, AITurnLogic.CityDefensePlan> combatAssignments,
        Dictionary<Unit, AITurnLogic.CityDefensePlan> scoutAssignments)
    {
        if (plan == null)
        {
            return;
        }

        if (combatAssignments != null)
        {
            if (plan.AssignedCombatUnit != null && !combatAssignments.ContainsKey(plan.AssignedCombatUnit))
            {
                combatAssignments.Add(plan.AssignedCombatUnit, plan);
            }

            if (plan.AssignedSupportCombatUnit != null && !combatAssignments.ContainsKey(plan.AssignedSupportCombatUnit))
            {
                combatAssignments.Add(plan.AssignedSupportCombatUnit, plan);
            }
        }

        if (scoutAssignments != null &&
            plan.AssignedScoutUnit != null &&
            !scoutAssignments.ContainsKey(plan.AssignedScoutUnit))
        {
            scoutAssignments.Add(plan.AssignedScoutUnit, plan);
        }
    }

    private List<MatchSnapshotHistoryStore.SnapshotHistoryGridCoord> BuildSnapshotHistoryVisibleTileCoords(
        HashSet<TileVisibility> visibleTiles)
    {
        List<MatchSnapshotHistoryStore.SnapshotHistoryGridCoord> coords =
            new List<MatchSnapshotHistoryStore.SnapshotHistoryGridCoord>();
        if (visibleTiles == null || visibleTiles.Count == 0)
        {
            return coords;
        }

        foreach (TileVisibility tile in visibleTiles)
        {
            if (tile == null)
            {
                continue;
            }

            coords.Add(new MatchSnapshotHistoryStore.SnapshotHistoryGridCoord
            {
                x = tile.gridX,
                y = tile.gridY
            });
        }

        coords.Sort((a, b) =>
        {
            int yCompare = a.y.CompareTo(b.y);
            return yCompare != 0 ? yCompare : a.x.CompareTo(b.x);
        });
        return coords;
    }

    private List<MatchSnapshotHistoryStore.SnapshotHistoryVisibleUnitSummary> BuildSnapshotHistoryVisibleEnemyUnits(
        bool actingSideIsPlayerOwned,
        HashSet<TileVisibility> visibleTiles)
    {
        List<MatchSnapshotHistoryStore.SnapshotHistoryVisibleUnitSummary> units =
            new List<MatchSnapshotHistoryStore.SnapshotHistoryVisibleUnitSummary>();
        if (gridManager == null)
        {
            return units;
        }

        Unit[] allUnits = Object.FindObjectsByType<Unit>();
        foreach (Unit unit in allUnits)
        {
            if (unit == null || unit.isPlayerOwned == actingSideIsPlayerOwned)
            {
                continue;
            }

            if (!gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile) ||
                tile == null ||
                visibleTiles == null ||
                !visibleTiles.Contains(tile))
            {
                continue;
            }

            units.Add(new MatchSnapshotHistoryStore.SnapshotHistoryVisibleUnitSummary
            {
                unitTypeId = unit.UnitTypeId,
                isPlayerOwned = unit.isPlayerOwned,
                x = tile.gridX,
                y = tile.gridY,
                currentHealthUnits = unit.currentHealthUnits,
                movesUsedThisTurn = unit.movesUsedThisTurn,
                attacksUsedThisTurn = unit.attacksUsedThisTurn
            });
        }

        units.Sort((a, b) =>
        {
            int yCompare = a.y.CompareTo(b.y);
            if (yCompare != 0)
            {
                return yCompare;
            }

            int xCompare = a.x.CompareTo(b.x);
            if (xCompare != 0)
            {
                return xCompare;
            }

            return string.Compare(a.unitTypeId, b.unitTypeId, System.StringComparison.Ordinal);
        });
        return units;
    }

    private List<MatchSnapshotHistoryStore.SnapshotHistoryCityThreatSummary> BuildSnapshotHistoryCityThreats(
        bool actingSideIsPlayerOwned,
        List<MatchSnapshotHistoryStore.SnapshotHistoryVisibleUnitSummary> visibleEnemyUnits)
    {
        List<MatchSnapshotHistoryStore.SnapshotHistoryCityThreatSummary> threatenedCities =
            new List<MatchSnapshotHistoryStore.SnapshotHistoryCityThreatSummary>();

        City[] cities = Object.FindObjectsByType<City>();
        foreach (City city in cities)
        {
            if (city == null || city.isPlayerOwned != actingSideIsPlayerOwned)
            {
                continue;
            }

            int visibleEnemyCountWithinThreatRadius = 0;
            int nearestVisibleEnemyDistance = int.MaxValue;
            for (int i = 0; i < visibleEnemyUnits.Count; i++)
            {
                MatchSnapshotHistoryStore.SnapshotHistoryVisibleUnitSummary enemyUnit = visibleEnemyUnits[i];
                int dx = Mathf.Abs(enemyUnit.x - city.x);
                int dy = Mathf.Abs(enemyUnit.y - city.y);
                int distance = Mathf.Max(dx, dy);
                if (distance < nearestVisibleEnemyDistance)
                {
                    nearestVisibleEnemyDistance = distance;
                }

                if (distance <= 2)
                {
                    visibleEnemyCountWithinThreatRadius++;
                }
            }

            if (visibleEnemyCountWithinThreatRadius <= 0)
            {
                continue;
            }

            threatenedCities.Add(new MatchSnapshotHistoryStore.SnapshotHistoryCityThreatSummary
            {
                x = city.x,
                y = city.y,
                visibleEnemyCountWithinThreatRadius = visibleEnemyCountWithinThreatRadius,
                nearestVisibleEnemyDistance = nearestVisibleEnemyDistance == int.MaxValue ? -1 : nearestVisibleEnemyDistance
            });
        }

        threatenedCities.Sort((a, b) =>
        {
            int yCompare = a.y.CompareTo(b.y);
            return yCompare != 0 ? yCompare : a.x.CompareTo(b.x);
        });
        return threatenedCities;
    }

    private string ResolveSnapshotHistorySourceForLoad(string debugSource)
    {
        if (isApplyingFetchedPlayByPostSnapshot)
        {
            return "fetch_remote";
        }

        if (string.IsNullOrWhiteSpace(debugSource))
        {
            return "load";
        }

        if (string.Equals(debugSource, "clipboard/transport", System.StringComparison.Ordinal))
        {
            return "load_json";
        }

        return "load_file";
    }

    private static PbpIncomingAudioUnitSummary BuildPbpIncomingAudioSummaryFromLiveUnits(Unit[] units)
    {
        PbpIncomingAudioUnitSummary summary = default;
        if (units == null)
            return summary;

        for (int i = 0; i < units.Length; i++)
        {
            Unit unit = units[i];
            if (unit == null || !unit.gameObject.activeInHierarchy)
                continue;

            if (unit.isPlayerOwned)
            {
                summary.playerUnitCount++;
            }
            else
            {
                summary.opponentUnitCount++;
            }
        }

        return summary;
    }

    private static PbpIncomingAudioUnitSummary BuildPbpIncomingAudioSummaryFromSavedUnits(List<SavedUnit> units)
    {
        PbpIncomingAudioUnitSummary summary = default;
        if (units == null)
            return summary;

        for (int i = 0; i < units.Count; i++)
        {
            SavedUnit unit = units[i];
            if (unit == null)
                continue;

            if (unit.isPlayerOwned)
            {
                summary.playerUnitCount++;
            }
            else
            {
                summary.opponentUnitCount++;
            }
        }

        return summary;
    }

    private void TryPresentIncomingPlayByPostAudio(PbpIncomingAudioUnitSummary beforeSummary, PbpIncomingAudioUnitSummary afterSummary)
    {
        if (!isApplyingFetchedPlayByPostSnapshot ||
            currentMode != GameMode.PlayByPost ||
            SoundManager.Instance == null)
        {
            return;
        }

        int playerDeaths = Mathf.Max(0, beforeSummary.playerUnitCount - afterSummary.playerUnitCount);
        int opponentDeaths = Mathf.Max(0, beforeSummary.opponentUnitCount - afterSummary.opponentUnitCount);
        int totalDeaths = playerDeaths + opponentDeaths;
        if (totalDeaths <= 0)
            return;

        int playbackCount = Mathf.Clamp(totalDeaths, 1, 2);
        if (pbpIncomingAudioRoutine != null)
        {
            StopCoroutine(pbpIncomingAudioRoutine);
            pbpIncomingAudioRoutine = null;
        }

        pbpIncomingAudioRoutine = StartCoroutine(PlayIncomingPbpDeathAudio(playbackCount));
    }

    private IEnumerator PlayIncomingPbpDeathAudio(int playbackCount)
    {
        int remaining = Mathf.Max(0, playbackCount);
        for (int i = 0; i < remaining; i++)
        {
            if (SoundManager.Instance == null)
            {
                pbpIncomingAudioRoutine = null;
                yield break;
            }

            SoundManager.Instance.PlayUnitDown();
            if (i < remaining - 1)
            {
                yield return new WaitForSecondsRealtime(0.08f);
            }
        }

        pbpIncomingAudioRoutine = null;
    }

    private bool TryBuildSaveJsonForDisk(out string json)
    {
        json = null;
        GameSave save = BuildCurrentSave();
        if (save == null)
            return false;

        json = JsonUtility.ToJson(save, true);
        return !string.IsNullOrWhiteSpace(json);
    }

    GameSave BuildCurrentSave()
    {
        if (gridManager == null)
        {
            Debug.LogWarning("Cannot build save: gridManager is null.");
            return null;
        }

        if (string.IsNullOrEmpty(currentGameId))
        {
            SetCurrentGameId(System.Guid.NewGuid().ToString());
        }

        GameSave save = new GameSave
        {
            gameId = currentGameId,
            protocolVersion = SupportedPbpProtocolVersion,
            appVersion = CurrentAppVersion,
            mode = currentMode.ToString(),
            aiRecruitVariant = aiRecruitVariant.ToString(),
            mapSizePreset = GetCurrentMapSizePreset().ToString(),
            boardWidth = gridManager.width,
            boardHeight = gridManager.height,
            currentTurnSeatIndex = GetAuthoritativeCurrentTurnSeatIndex(),
            isPlayerTurn = GetAuthoritativeCurrentTurnSeatIndex() == 0,
            seatGold = BuildSeatGoldSnapshot(GetRuntimeSeatCount()),
            turnNumber = turnNumber,
            playerGold = playerGold,
            aiGold = aiGold,
            gameOver = gameOver,
            hasWinnerSeatIndex = currentMode == GameMode.PlayByPost && gameOver && pbpWinnerSeatIndex >= 0,
            winnerSeatIndex = currentMode == GameMode.PlayByPost && gameOver && pbpWinnerSeatIndex >= 0
                ? PlayByPostSeatUtility.NormalizeSeatIndex(pbpWinnerSeatIndex, GetRuntimeSeatCount())
                : -1,
            visibilityRadius = visibilityRadius
        };

        ApplyTypedDisplayNameMetadata(save);
        ApplyPlayByPostSeatMetadata(save);

        // Cities
        City[] cities = Object.FindObjectsByType<City>();
        foreach (City city in cities)
        {
            save.cities.Add(new SavedCity
            {
                x = city.x,
                y = city.y,
                ownerSeatIndex = city.ownerSeatIndex,
                isPlayerOwned = city.isPlayerOwned,
                hasRecruitedThisTurn = city.hasRecruitedThisTurn
            });
        }

        // Units
        Unit[] units = Object.FindObjectsByType<Unit>();
        foreach (Unit unit in units)
        {
            Vector3 pos = unit.transform.position;
            save.units.Add(new SavedUnit
            {
                unitTypeId = unit.UnitTypeId,
                ownerSeatIndex = unit.ownerSeatIndex,
                isPlayerOwned = unit.isPlayerOwned,
                x = pos.x,
                y = pos.y,
                z = pos.z,
                currentHealthUnits = unit.currentHealthUnits,
                movesUsedThisTurn = unit.movesUsedThisTurn,
                attacksUsedThisTurn = unit.attacksUsedThisTurn,
                hasAttackedThisTurn = unit.attacksUsedThisTurn > 0
            });
        }

        // Tiles (seen state per side)
        foreach (TileVisibility tile in gridManager.GetAllTiles())
        {
            List<int> seenSeatIndices = new List<int>();
            tile.GetSeenSeatIndices(seenSeatIndices);
            save.tiles.Add(new SavedTile
            {
                x = tile.gridX,
                y = tile.gridY,
                seenSeatIndices = seenSeatIndices,
                playerSeen = seenSeatIndices.Contains(0),
                opponentSeen = seenSeatIndices.Contains(1)
            });
        }

        return save;
    }

    /// <summary>
    /// Copy the current game state as JSON into the clipboard (for Play-by-Post).
    /// </summary>
    public void CopyCurrentStateToClipboard()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!TryBuildPlayByPostExportJson(out _, out string json))
        {
            return;
        }

        if (ClipboardUtility.TryCopy(json))
        {
            Debug.Log($"Play-by-Post JSON copied to clipboard ({json.Length} chars).");
        }
        else
        {
            Debug.LogWarning($"Failed to copy Play-by-Post JSON to clipboard ({json.Length} chars). On WebGL this may require user interaction/permissions.");
        }
#endif
    }

    /// <summary>
    /// Copy the current Play-by-Post game id to the clipboard.
    /// </summary>
    public void CopyCurrentGameIdToClipboard()
    {
        string gameId = currentGameId;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            gameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        }

        if (string.IsNullOrWhiteSpace(gameId))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("CopyCurrentGameIdToClipboard: no gameId available.");
#endif
            return;
        }

        if (!ClipboardUtility.TryCopy(gameId))
        {
            GUIUtility.systemCopyBuffer = gameId;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"CopyCurrentGameIdToClipboard: ClipboardUtility failed, fallback used ({gameId}).");
#endif
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"Play-by-Post game id copied to clipboard ({gameId}).");
#endif
    }

    internal bool TryBuildPlayByPostExportJson(out int exportTurnNumber, out string json)
    {
        exportTurnNumber = 0;
        json = null;

        if (!TryBuildPlayByPostExportSave(out GameSave saveForExport, out string builtJson))
        {
            return false;
        }

        json = builtJson;
        exportTurnNumber = saveForExport.turnNumber;
        SaveManifestService.RecordPlayByPostExport(
            currentGameId,
            null,
            lastKnownRoundTurn: saveForExport.turnNumber,
            lastKnownIsPlayerTurn: saveForExport.isPlayerTurn,
            lastKnownCurrentTurnSeatIndex: saveForExport.currentTurnSeatIndex,
            lastKnownTransportSeq: ComputeTransportSeq(saveForExport),
            lastKnownSeatCount: saveForExport.seatCount);
        return true;
    }

    private static int ComputeTransportSeq(GameSave s)
    {
        if (s != null && s.transportSeq > 0)
        {
            return s.transportSeq;
        }

        int seatCount = s != null ? s.seatCount : PlayByPostSeatUtility.MinSeatCount;
        int currentSeatIndex = 0;
        if (s != null)
        {
            if (s.protocolVersion >= SupportedPbpProtocolVersion)
            {
                currentSeatIndex = s.currentTurnSeatIndex;
            }
            else
            {
                currentSeatIndex = s.isPlayerTurn ? 0 : 1;
            }
        }

        return ComputeTransportSeq(
            s.turnNumber,
            currentSeatIndex,
            seatCount);
    }

    private static int ComputeTransportSeq(int roundTurn, bool turnIsPlayer)
    {
        return ComputeTransportSeq(roundTurn, turnIsPlayer ? 0 : 1, PlayByPostSeatUtility.MinSeatCount);
    }

    private static int ComputeTransportSeq(int roundTurn, int turnSeatIndex, int seatCount)
    {
        int clampedRoundTurn = System.Math.Max(0, roundTurn);
        int normalizedSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(seatCount);
        int normalizedSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(turnSeatIndex, normalizedSeatCount);
        return clampedRoundTurn * normalizedSeatCount + normalizedSeatIndex;
    }

    private int ResolveAIGoldIncome(int baseIncome, int roundTurnNumber)
    {
        return Mathf.Max(0, baseIncome);
    }

    private MapSizePreset GetCurrentMapSizePreset()
    {
        if (gridManager == null)
        {
            return GetDefaultMapSizePreset();
        }

        return ResolveMapSizePreset(gridManager.width, gridManager.height);
    }

    private static void ResolveBoardSizeFromSave(GameSave save, out MapSizePreset preset, out int boardWidth, out int boardHeight)
    {
        preset = save != null ? ParseMapSizePresetOrDefault(save.mapSizePreset) : GetDefaultMapSizePreset();

        GetBoardDimensionsForPreset(preset, out int presetWidth, out int presetHeight);
        boardWidth = save != null && save.boardWidth > 0 ? save.boardWidth : presetWidth;
        boardHeight = save != null && save.boardHeight > 0 ? save.boardHeight : presetHeight;

        if (boardWidth <= 0 || boardHeight <= 0)
        {
            boardWidth = presetWidth;
            boardHeight = presetHeight;
        }
    }

    private void EnsureBoardDimensions(int boardWidth, int boardHeight)
    {
        if (gridManager == null)
        {
            return;
        }

        if (gridManager.HasDimensions(boardWidth, boardHeight) &&
            gridManager.tileGrid != null &&
            gridManager.tileGrid.Length > 0)
        {
            return;
        }

        gridManager.RebuildGrid(boardWidth, boardHeight);
    }

    private bool TryBuildPlayByPostExportSave(out GameSave saveForExport, out string json)
    {
        saveForExport = null;
        json = null;

        GameSave current = BuildCurrentSave();
        if (current == null)
        {
            return false;
        }

        saveForExport = current;

        // For Play-by-Post we build a snapshot that already represents the *next* side's turn
        // so the receiving player can simply load and start playing.
        if (currentMode == GameMode.PlayByPost)
        {
            // Deep-copy via JSON so we don't mutate live state.
            string tmp = JsonUtility.ToJson(current);
            saveForExport = JsonUtility.FromJson<GameSave>(tmp);
            PreparePlayByPostNextTurnSnapshot(saveForExport);
            if (saveForExport != null &&
                saveForExport.gameOver &&
                !saveForExport.hasWinnerSeatIndex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError(
                    $"PBp export build missing authoritative winner seat (gameId={saveForExport.gameId ?? "<none>"}, turn={saveForExport.turnNumber}).");
#endif
                json = null;
                return false;
            }
        }

        json = JsonUtility.ToJson(saveForExport, playByPostExportPretty);
        return !string.IsNullOrWhiteSpace(json);
    }

    public void PlayByPostSyncNow()
    {
        if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
        {
            Debug.Log($"PlayByPostSyncNow called (mode={currentMode}, waiting={isPlayByPostWaitingForExport})");
        }
        if (!isPlayByPostWaitingForExport || currentMode != GameMode.PlayByPost)
            return;

        ResolveTurnTransport();
        StartCoroutine(TryFetchPlayByPostTurnOnce());   
    }
    
    #if UNITY_EDITOR
[ContextMenu("PBp Debug Sync Now")]
private void PBpDebugSyncNow_Context()
{
    if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
    {
        Debug.Log("PBp Debug Context Sync triggered");
    }
    PlayByPostSyncNow();
}
#endif

    public bool LoadFromFile(string path = null)
    {
        string targetPath = path;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            targetPath = GetDefaultSavePath();
        }
        targetPath = NormalizeSavePath(targetPath);
        LogPlayByPostTelemetry(
            "LoadFromFileStart",
            $"path={targetPath} state={GetPlayByPostStateSummary()}");

        if (!File.Exists(targetPath))
        {
            Debug.LogWarning("No save file found at " + targetPath);
            return false;
        }

        if (gridManager == null)
        {
            Debug.LogWarning("Cannot load: gridManager is null.");
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(targetPath);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to read save at {targetPath}: {ex.Message}");
            return false;
        }
        GameSave save;
        isLoadingFromSave = true;
        try
        {
            save = JsonUtility.FromJson<GameSave>(json);
        }
        catch (System.Exception ex)
        {
            isLoadingFromSave = false;
            Debug.LogError("Failed to parse save: " + ex.Message);
            return false;
        }

        if (save != null &&
            string.Equals(save.mode, GameMode.PlayByPost.ToString(), System.StringComparison.Ordinal))
        {
            LogPlayByPostTelemetry(
                "LoadFromFileParsed",
                $"path={targetPath} loadedMode={save.mode} loadedGameId={save.gameId} loadedRoundTurn={save.turnNumber} loadedIsPlayerTurn={save.isPlayerTurn} loadedTransportSeq={ComputeTransportSeq(save)}");
        }

        return ApplyLoadedSave(save, targetPath, json);
    }

    public bool LoadFromJsonString(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning("LoadFromJsonString: json is empty.");
            return false;
        }

        if (gridManager == null)
        {
            Debug.LogWarning("Cannot load JSON: gridManager is null.");
            return false;
        }

        GameSave save;
        isLoadingFromSave = true;
        try
        {
            save = JsonUtility.FromJson<GameSave>(json);
        }
        catch (System.Exception ex)
        {
            isLoadingFromSave = false;
            Debug.LogError("Failed to parse JSON save: " + ex.Message);
            return false;
        }

        if (save != null &&
            string.Equals(save.mode, GameMode.PlayByPost.ToString(), System.StringComparison.Ordinal))
        {
            LogPlayByPostTelemetry(
                "LoadFromJsonParsed",
                $"source=clipboard/transport jsonLen={json.Length} loadedMode={save.mode} loadedGameId={save.gameId} loadedRoundTurn={save.turnNumber} loadedIsPlayerTurn={save.isPlayerTurn} loadedTransportSeq={ComputeTransportSeq(save)}");
        }

        return ApplyLoadedSave(save, "clipboard/transport", json);
    }

    private bool ApplyLoadedSave(GameSave save, string debugSource, string rawLoadedJson = null)
    {
        try
        {
            if (save == null)
            {
                Debug.LogError("Load failed: save was null.");
                return false;
            }

            bool loadedModeIsPbp = string.Equals(
                save.mode,
                GameMode.PlayByPost.ToString(),
                System.StringComparison.Ordinal);
            bool shouldPresentIncomingPbpAudio = loadedModeIsPbp && isApplyingFetchedPlayByPostSnapshot;
            int migrationSourceProtocolVersion = 0;
            if (loadedModeIsPbp)
            {
                string loadedGameId = string.IsNullOrWhiteSpace(save.gameId) ? "<none>" : save.gameId;
                if (!TryValidatePbpLoadProtocol(
                        save,
                        out int loadedProtocolVersion,
                        out migrationSourceProtocolVersion,
                        out string protocolError))
                {
                    Debug.LogError($"{protocolError} (gameId={loadedGameId}).");
                    TryShowPbpVersionMismatchInGamePopup(loadedProtocolVersion, save.gameId, debugSource);
                    return false;
                }

                if (!TryValidatePbpLoadAppVersion(
                        save,
                        out string loadedAppVersion,
                        out string appVersionError))
                {
                    Debug.LogError($"{appVersionError} (gameId={loadedGameId}).");
                    TryShowPbpAppVersionMismatchInGamePopup(loadedAppVersion, save.gameId, debugSource);
                    return false;
                }
            }

            if (string.Equals(save.mode, GameMode.PlayByPost.ToString(), System.StringComparison.Ordinal) ||
                currentMode == GameMode.PlayByPost)
            {
                LogPlayByPostTelemetry(
                    "ApplyLoadedSaveStart",
                    $"source={debugSource} loadedMode={save.mode} loadedGameId={save.gameId} loadedRoundTurn={save.turnNumber} loadedIsPlayerTurn={save.isPlayerTurn} loadedTransportSeq={ComputeTransportSeq(save)} stateBefore={GetPlayByPostStateSummary()}");
            }

            save.tiles ??= new List<SavedTile>();
            save.units ??= new List<SavedUnit>();
            save.cities ??= new List<SavedCity>();

            ResolveBoardSizeFromSave(save, out _, out int savedBoardWidth, out int savedBoardHeight);
            EnsureBoardDimensions(savedBoardWidth, savedBoardHeight);

            // Basic grid validation: ensure saved tiles fit current grid.
            int maxTileX = -1;
            int maxTileY = -1;
            foreach (SavedTile t in save.tiles)
            {
                if (t.x > maxTileX) maxTileX = t.x;
                if (t.y > maxTileY) maxTileY = t.y;
            }
            if (maxTileX >= gridManager.width || maxTileY >= gridManager.height)
            {
                Debug.LogError($"Save grid ({maxTileX + 1}x{maxTileY + 1}) does not fit current grid ({gridManager.width}x{gridManager.height}). Aborting load.");
                return false;
            }

            // Apply basic state.
            if (!System.Enum.TryParse(save.mode, out GameMode loadedMode))
            {
                Debug.LogError($"Unsupported save mode '{save.mode}'. Load aborted.");
                return false;
            }

            currentMode = loadedMode;
            configuredPlayByPostSeatCount = currentMode == GameMode.PlayByPost
                ? PlayByPostSeatUtility.NormalizeSeatCount(save.seatCount)
                : PlayByPostSeatUtility.MinSeatCount;

            aiRecruitVariant = AIRecruitVariant.Default;
            if (!string.IsNullOrEmpty(save.aiRecruitVariant) &&
                System.Enum.TryParse(save.aiRecruitVariant, out AIRecruitVariant loadedRecruitVariant))
            {
                aiRecruitVariant = loadedRecruitVariant;
            }

            SetCurrentGameId(string.IsNullOrEmpty(save.gameId) ? System.Guid.NewGuid().ToString() : save.gameId);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ResolveAIVsAIDebugRuntimeSettings(consumePendingSelection: false);
#endif
            if (currentMode == GameMode.PlayByPost)
            {
                PersistCurrentPbpGameIdIfNeeded();
            }
            UpdateKnownTypedDisplayNames(save);
            int runtimeSeatCount = currentMode == GameMode.PlayByPost
                ? configuredPlayByPostSeatCount
                : PlayByPostSeatUtility.MinSeatCount;
            SetCurrentTurnSeatIndexForRuntime(
                currentMode == GameMode.PlayByPost
                    ? ResolveCurrentTurnSeatIndex(save, runtimeSeatCount)
                    : (save.isPlayerTurn ? 0 : 1),
                runtimeSeatCount);
            turnNumber = save.turnNumber;
            SetSeatGoldStateFromList(ResolveSeatGold(save, runtimeSeatCount), runtimeSeatCount);
            gameOver = save.gameOver;
            if (currentMode == GameMode.PlayByPost && gameOver && save.hasWinnerSeatIndex)
            {
                pbpWinnerSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(save.winnerSeatIndex, runtimeSeatCount);
            }
            else
            {
                pbpWinnerSeatIndex = -1;
            }
            if (!gameOver)
            {
                ResetGameOverUiState();
            }
            else
            {
                SetGameOverUiMessage(DefaultGameOverMessage);
                if (currentMode != GameMode.PlayByPost)
                {
                    gameOverUiPrimaryButtonLabel = DefaultGameOverPrimaryButtonLabel;
                    gameOverUiPrimaryButtonInteractable = true;
                }
                else
                {
                    gameOverUiPrimaryButtonLabel = DefaultGameOverPrimaryButtonLabel;
                    gameOverUiPrimaryButtonInteractable = false;
                }
            }
            visibilityRadius = save.visibilityRadius;
            isPlayByPostWaitingForExport = false;
            Time.timeScale = 1f;
            int viewerSeatIndexForLoad = GetViewerSeatIndexForRuntime();
            int unitCountBeforeClear = 0;
            int unitCountAfterSpawn = 0;
            int duplicateOwnerTileSlots = 0;
            string snapshotWriteMode = "none";
            bool recordedLoadAppliedViaFetchedSnapshotIngest = false;
            int pbpSeat = 0;
            bool pbpHasSeat = false;
            string pbpSeatTextForLog = "<none>";
            PbpIncomingAudioUnitSummary incomingPbpBeforeSummary = default;
            PbpIncomingAudioUnitSummary incomingPbpAfterSummary = default;

            // Clear units
            Unit[] existingUnits = Object.FindObjectsByType<Unit>();
            if (shouldPresentIncomingPbpAudio)
            {
                incomingPbpBeforeSummary = BuildPbpIncomingAudioSummaryFromLiveUnits(existingUnits);
                incomingPbpAfterSummary = BuildPbpIncomingAudioSummaryFromSavedUnits(save.units);
            }

            foreach (Unit u in existingUnits)
            {
                if (u != null)
                {
                    unitCountBeforeClear++;
                    GameObject unitObject = u.gameObject;
                    if (unitObject != null)
                    {
                        // Destroy is deferred to end-of-frame; disable immediately so stale units
                        // cannot be interacted with or picked by same-frame queries.
                        unitObject.SetActive(false);
                        Destroy(unitObject);
                    }
                }
            }

            // Restore cities
            City[] cities = Object.FindObjectsByType<City>();
            foreach (City city in cities)
            {
                city.stationedUnit = null;
            }

            foreach (SavedCity c in save.cities)
            {
                foreach (City city in cities)
                {
                    if (city.x == c.x && city.y == c.y)
                    {
                        city.SetOwnerSeatIndex(ResolveOwnerSeatIndex(c.ownerSeatIndex, c.isPlayerOwned, runtimeSeatCount));
                        city.hasRecruitedThisTurn = c.hasRecruitedThisTurn;
                    }
                }
            }

            foreach (SavedUnit u in save.units)
            {
                if (!TryResolveLoadedUnitTypeId(u.unitTypeId, loadedModeIsPbp, out string resolvedUnitTypeId, out string typeResolutionError))
                {
                    Debug.LogError(typeResolutionError);
                    return false;
                }

                GameObject prefab = GetUnitPrefabForType(resolvedUnitTypeId);
                if (prefab == null)
                {
                    Debug.LogError($"No unit prefab configured for unit type '{resolvedUnitTypeId}'. Cannot restore units; load aborted.");
                    return false;
                }

                Vector3 pos = new Vector3(u.x, u.y, u.z);
                GameObject go = InstantiateConfiguredUnit(
                    resolvedUnitTypeId,
                    prefab,
                    pos,
                    ResolveOwnerSeatIndex(u.ownerSeatIndex, u.isPlayerOwned, runtimeSeatCount),
                    currentCity: null,
                    resetTurnState: false);
                if (go == null)
                {
                    Debug.LogError($"Failed to instantiate unit type '{resolvedUnitTypeId}' while loading.");
                    return false;
                }

                Unit unit = go.GetComponent<Unit>();
                if (unit != null)
                {
                    unit.ApplyDefinition(resolvedUnitTypeId, preserveCurrentHealth: false);
                    unit.SetCurrentHealthUnits(ResolveLoadedCurrentHealthUnits(u));
                    unit.movesUsedThisTurn = Mathf.Clamp(u.movesUsedThisTurn, 0, unit.maxMovesPerTurn);
                    int loadedAttacksUsedThisTurn = u.attacksUsedThisTurn > 0
                        ? u.attacksUsedThisTurn
                        : (u.hasAttackedThisTurn ? 1 : 0);
                    unit.attacksUsedThisTurn = Mathf.Clamp(loadedAttacksUsedThisTurn, 0, unit.maxAttacksPerTurn);
                    bool isCurrentSideUnit = true;
                    if (currentMode == GameMode.PlayByPost)
                    {
                        isCurrentSideUnit = unit.ownerSeatIndex == viewerSeatIndexForLoad;
                    }
                    unit.SetFogVisibility(true, isCurrentSideUnit); // will be updated after visibility recalculation
                }

                // Link to city if occupying one
                foreach (City city in cities)
                {
                    if (Vector3.SqrMagnitude(city.transform.position - pos) < 0.001f)
                    {
                        city.stationedUnit = go;
                        if (unit != null)
                        {
                            unit.currentCity = city;
                        }
                        break;
                    }
                }
            }

            Unit[] unitsAfterSpawn = Object.FindObjectsByType<Unit>();
            unitCountAfterSpawn = unitsAfterSpawn != null ? unitsAfterSpawn.Length : 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            duplicateOwnerTileSlots = CountDuplicateOwnerTileSlots(unitsAfterSpawn);
            if (duplicateOwnerTileSlots > 0)
            {
                Debug.LogWarning(
                    $"[PBpLoadDupCheck] source={debugSource} gameId={currentGameId} turn={turnNumber} isPlayerTurn={isPlayerTurn} duplicateOwnerTileSlots={duplicateOwnerTileSlots}");
            }
#endif

            // Update move outlines for the active side based on loaded move state
            if (UnitSelectionManager.Instance != null)
            {
                UnitSelectionManager.Instance.ClearSelection();
                UnitSelectionManager.Instance.RefreshMoveOutlinesForCurrentTurn();
            }

            // Restore tile seen state.
            // For Play-by-Post we keep things simpler and recompute fog
            // purely from current cities/units, ignoring remembered
            // exploration from the save to avoid asymmetric artefacts.
            foreach (TileVisibility tile in gridManager.GetAllTiles())
            {
                tile.ResetVisibilityState();
            }

            if (currentMode != GameMode.PlayByPost)
            {
                foreach (SavedTile t in save.tiles)
                {
                    if (gridManager.TryGetTile(t.x, t.y, out TileVisibility tile))
                    {
                        tile.SetSeenSeatIndices(ResolveSeenSeatIndices(t), 0);
                    }
                }
            }

            // After loading, ensure selection is cleared and move outlines
            // reflect the loaded movement state.
            if (UnitSelectionManager.Instance != null)
            {
                UnitSelectionManager.Instance.ClearSelection();
                UnitSelectionManager.Instance.RefreshMoveOutlinesForCurrentTurn();
            }

            RecalculatePlayerVisibility();
            if (currentMode == GameMode.PlayByPost)
            {
                lastAppliedTurnNumberForPolling = ComputeTransportSeq(save);
                EnsurePlayByPostControlReadiness();
                pbpHasSeat = TryGetLocalSeatIndexForPbp(currentGameId, out pbpSeat);
                bool localTurn = pbpHasSeat && pbpSeat == currentTurnSeatIndex;
                pbpSeatTextForLog = pbpHasSeat ? pbpSeat.ToString() : "<none>";
                isPlayByPostWaitingForExport = !localTurn;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
                {
                    Debug.Log(
                        $"[PBpLoadSeat] loadedGameId={save.gameId} seat={pbpSeatTextForLog} hasSeat={pbpHasSeat} viewerSeatIndex={viewerSeatIndexForLoad} currentTurnSeatIndex={currentTurnSeatIndex} loadedIsPlayerTurn={isPlayerTurn} isWaitingForExport={isPlayByPostWaitingForExport} canAdvance={CanAdvanceTurn()}");
                }
#endif
                if (!localTurn)
                {
                    if (playByPostAutoSyncEnabled && playByPostPollRoutine == null)
                    {
                        ResolveTurnTransport();
                        StartPlayByPostPolling(lastAppliedTurnNumberForPolling);
                    }
                }
            }
            else
            {
                lastAppliedTurnNumberForPolling = turnNumber;
            }
            RefreshEndTurnButtonInteractable(force: true);
            if (shouldPresentIncomingPbpAudio)
            {
                TryPresentIncomingPlayByPostAudio(incomingPbpBeforeSummary, incomingPbpAfterSummary);
            }

            if (currentMode == GameMode.PlayByPost)
            {
                if (!string.IsNullOrWhiteSpace(rawLoadedJson))
                {
                    string historySource = migrationSourceProtocolVersion == SupportedPbpMigrationProtocolVersion
                        ? "migrated_rewrite"
                        : ResolveSnapshotHistorySourceForLoad(debugSource);
                    if (TryIngestFetchedPlayByPostSnapshotJson(
                            rawLoadedJson,
                            currentGameId,
                            ComputeTransportSeq(turnNumber, GetAuthoritativeCurrentTurnSeatIndex(), GetRuntimeSeatCount()),
                            GetConfiguredPlayByPostSeatCount(),
                            historySource,
                            out _,
                            out _,
                            out _,
                            out _,
                            out _,
                            out _))
                    {
                        snapshotWriteMode = migrationSourceProtocolVersion == SupportedPbpMigrationProtocolVersion
                            ? "migratedIngest"
                            : "canonicalIngest";
                        recordedLoadAppliedViaFetchedSnapshotIngest = true;
                    }
                    else
                    {
                        SavePlayByPostPerGameSnapshot(historySource: historySource);
                        snapshotWriteMode = migrationSourceProtocolVersion == SupportedPbpMigrationProtocolVersion
                            ? "migratedRebuildFallback"
                            : "canonicalRebuildFallback";
                    }
                }
                else
                {
                    SavePlayByPostPerGameSnapshot(historySource: "load_rebuild");
                    snapshotWriteMode = "rebuild";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning(
                        $"[PBpLoadApply] source={debugSource} gameId={currentGameId} rawJsonMissing=true; falling back to snapshot rebuild.");
#endif
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
                {
                    Debug.Log(
                        $"[PBpLoadApply] source={debugSource} gameId={currentGameId} seat={pbpSeatTextForLog} turn={turnNumber} isPlayerTurn={isPlayerTurn} unitsBeforeClear={unitCountBeforeClear} unitsAfterSpawn={unitCountAfterSpawn} duplicateOwnerTileSlots={duplicateOwnerTileSlots} snapshotWrite={snapshotWriteMode}");
                }
#endif
                LogPlayByPostTelemetry(
                    "ApplyLoadedSaveDone",
                    $"source={debugSource} snapshotWrite={snapshotWriteMode} loadRelationToLastSubmit={GetPlayByPostLoadRelationToLastSubmit()} stateAfter={GetPlayByPostStateSummary()}");
            }
            if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
            {
                Debug.Log("Game loaded from " + debugSource);
            }

            if (currentMode != GameMode.PlayByPost)
            {
                string historySource = ResolveSnapshotHistorySourceForLoad(debugSource);
                if (!string.IsNullOrWhiteSpace(rawLoadedJson))
                {
                    TryCaptureSnapshotHistoryCopy(
                        currentGameId,
                        rawLoadedJson,
                        turnNumber,
                        isPlayerTurn,
                        debugSource,
                        historySource);
                }
            }

            if (!recordedLoadAppliedViaFetchedSnapshotIngest)
            {
                SaveManifestService.RecordLoadApplied(
                    currentGameId,
                    currentMode,
                    gameOver,
                    lastKnownRoundTurn: turnNumber,
                    lastKnownIsPlayerTurn: isPlayerTurn,
                    lastKnownCurrentTurnSeatIndex: GetAuthoritativeCurrentTurnSeatIndex(),
                    lastKnownTransportSeq: ComputeTransportSeq(turnNumber, GetAuthoritativeCurrentTurnSeatIndex(), GetRuntimeSeatCount()),
                    lastKnownSeatCount: GetConfiguredPlayByPostSeatCount());
            }
            GameplayInputOrchestrator.ResetTransientInputState();

            if (currentMode == GameMode.VsAI && !gameOver)
            {
                if (IsAIVsAIDebugModeActive())
                {
                    StartAIVsAIDebugLoopIfNeeded();
                }
                else if (!isPlayerTurn)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log("[Turn] Resuming AI turn after load.");
#endif
                    StartCoroutine(AITurn());
                }
            }

            if (gameOver)
            {
                if (currentMode == GameMode.PlayByPost)
                {
                    ConfigurePbpEndgameFromLoadedState();
                }
                else
                {
                    ShowGameOverPopup(DefaultGameOverMessage, writeLog: false);
                }
            }

            ScheduleAutoEndTurnCheck();
            StartAIVsAIDebugLoopIfNeeded();
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("ApplyLoadedSave failed: " + ex.Message);
            return false;
        }
        finally
        {
            isLoadingFromSave = false;
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private int CountDuplicateOwnerTileSlots(Unit[] units)
    {
        if (units == null || units.Length == 0)
            return 0;

        var countsByOwnerAndPosition = new Dictionary<string, int>();
        int duplicateSlots = 0;
        for (int i = 0; i < units.Length; i++)
        {
            Unit unit = units[i];
            if (unit == null)
                continue;

            Vector3 pos = unit.transform.position;
            int x = Mathf.RoundToInt(pos.x * 1000f);
            int y = Mathf.RoundToInt(pos.y * 1000f);
            int z = Mathf.RoundToInt(pos.z * 1000f);
            string key = $"{(unit.isPlayerOwned ? 1 : 0)}:{x}:{y}:{z}";

            if (!countsByOwnerAndPosition.TryGetValue(key, out int count))
            {
                countsByOwnerAndPosition[key] = 1;
                continue;
            }

            count++;
            countsByOwnerAndPosition[key] = count;
            if (count == 2)
            {
                duplicateSlots++;
            }
        }

        return duplicateSlots;
    }
#endif

    public bool TrySpendGoldForSeat(int ownerSeatIndex, int amount)
    {
        if (amount <= 0)
            return true;

        bool shouldPlayInvalid = false;
        if (SoundManager.Instance != null && !gameOver && !ShouldSuppressAIVsAIAudio())
        {
            if (currentMode == GameMode.VsAI)
            {
                // Only play invalid for the human player's gold in Vs AI.
                shouldPlayInvalid = ownerSeatIndex == 0 && isPlayerTurn;
            }
            else if (currentMode == GameMode.PlayByPost)
            {
                bool spendingSideIsActive = currentTurnSeatIndex == ownerSeatIndex;
                shouldPlayInvalid = spendingSideIsActive && IsHumanTurn();
            }
            else
            {
                shouldPlayInvalid = ownerSeatIndex == 0;
            }
        }

        int normalizedSeatIndex = Mathf.Max(0, ownerSeatIndex);
        EnsureSeatGoldCapacity(Mathf.Max(GetRuntimeSeatCount(), normalizedSeatIndex + 1));
        if (seatGold[normalizedSeatIndex] < amount)
        {
            if (shouldPlayInvalid)
            {
                SoundManager.Instance.PlayInvalid();
            }

            return false;
        }

        seatGold[normalizedSeatIndex] -= amount;
        SyncLegacyGoldBridge();
        return true;
    }

    public bool TrySpendGold(bool forPlayer, int amount)
    {
        return TrySpendGoldForSeat(forPlayer ? 0 : 1, amount);
    }

    public void AddGoldForSeat(int ownerSeatIndex, int amount)
    {
        if (amount <= 0)
            return;

        int normalizedSeatIndex = Mathf.Max(0, ownerSeatIndex);
        EnsureSeatGoldCapacity(Mathf.Max(GetRuntimeSeatCount(), normalizedSeatIndex + 1));
        seatGold[normalizedSeatIndex] += amount;
        SyncLegacyGoldBridge();
    }

    public void AddGold(bool forPlayer, int amount)
    {
        AddGoldForSeat(forPlayer ? 0 : 1, amount);
    }

    /// <summary>
    /// Mutates the given save so that it represents the start
    /// of the next side's turn for Play-by-Post exports.
    /// </summary>
    private void PreparePlayByPostNextTurnSnapshot(GameSave save)
    {
        int seatCount = PlayByPostSeatUtility.NormalizeSeatCount(save.seatCount);
        int activeSeatIndex = ResolveCurrentTurnSeatIndex(save, seatCount);
        List<PlayByPostSeatMetadata> normalizedSeats = BuildNormalizedPlayByPostSeatMetadata(
            save.seats,
            seatCount,
            save.playerOneTypedDisplayName,
            save.playerTwoTypedDisplayName);
        ResolveAdvancedPlayByPostTurnState(
            save,
            normalizedSeats,
            seatCount,
            activeSeatIndex,
            out int nextSeatIndex,
            out int nextTurnNumber,
            out _,
            out int eligibleSeatCount);
        save.currentTurnSeatIndex = nextSeatIndex;
        save.isPlayerTurn = nextSeatIndex == 0;
        save.turnNumber = nextTurnNumber;
        if (eligibleSeatCount <= 1)
        {
            save.gameOver = true;
        }

        List<int> resolvedSeatGold = ResolveSeatGold(save, seatCount);
        save.seatGold = new List<int>(resolvedSeatGold);
        save.playerGold = save.seatGold.Count > 0 ? save.seatGold[0] : 0;
        save.aiGold = save.seatGold.Count > 1 ? save.seatGold[1] : 0;

        if (save.gameOver)
        {
            ApplyPlayByPostSeatMetadata(save, refreshLocalSeatTypedDisplayName: true);
            return;
        }

        int income = 0;
        foreach (SavedCity city in save.cities)
        {
            int ownerSeatIndex = ResolveOwnerSeatIndex(city.ownerSeatIndex, city.isPlayerOwned, seatCount);
            city.ownerSeatIndex = ownerSeatIndex;
            city.isPlayerOwned = ownerSeatIndex == 0;
            if (ownerSeatIndex == nextSeatIndex)
            {
                city.hasRecruitedThisTurn = false;
                income += goldPerCity;
            }
        }

        if (income > 0)
        {
            int appliedIncome = nextSeatIndex == 1
                ? ResolveAIGoldIncome(income, save.turnNumber)
                : income;
            while (save.seatGold.Count <= nextSeatIndex)
            {
                save.seatGold.Add(0);
            }

            save.seatGold[nextSeatIndex] += appliedIncome;
        }

        foreach (SavedUnit unit in save.units)
        {
            int ownerSeatIndex = ResolveOwnerSeatIndex(unit.ownerSeatIndex, unit.isPlayerOwned, seatCount);
            unit.ownerSeatIndex = ownerSeatIndex;
            unit.isPlayerOwned = ownerSeatIndex == 0;
            if (ownerSeatIndex == nextSeatIndex)
            {
                unit.movesUsedThisTurn = 0;
                unit.attacksUsedThisTurn = 0;
                unit.hasAttackedThisTurn = false;
            }
        }

        save.playerGold = save.seatGold.Count > 0 ? save.seatGold[0] : 0;
        save.aiGold = save.seatGold.Count > 1 ? save.seatGold[1] : 0;
        ApplyPlayByPostSeatMetadata(save, refreshLocalSeatTypedDisplayName: true);
    }

    public static bool TryBuildPlayByPostResignationJson(
        string json,
        string expectedGameId,
        int localSeatIndex,
        string claimedPlayerId,
        string typedDisplayName,
        out string resignedJson,
        out int exportTurnNumber,
        out bool exportIsPlayerTurn,
        out int exportCurrentTurnSeatIndex,
        out int exportTransportSeq,
        out int exportSeatCount,
        out bool exportGameOver)
    {
        resignedJson = json;
        exportTurnNumber = 0;
        exportIsPlayerTurn = false;
        exportCurrentTurnSeatIndex = 0;
        exportTransportSeq = 0;
        exportSeatCount = PlayByPostSeatUtility.MinSeatCount;
        exportGameOver = false;

        if (string.IsNullOrWhiteSpace(json) ||
            localSeatIndex < 0 ||
            localSeatIndex >= PlayByPostSeatUtility.MaxSeatCount)
        {
            return false;
        }

        GameSave save;
        try
        {
            save = JsonUtility.FromJson<GameSave>(json);
        }
        catch
        {
            return false;
        }

        if (save == null ||
            !string.Equals(save.mode, GameMode.PlayByPost.ToString(), System.StringComparison.Ordinal) ||
            save.gameOver)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedGameId) &&
            !string.IsNullOrWhiteSpace(save.gameId) &&
            !string.Equals(save.gameId, expectedGameId, System.StringComparison.Ordinal))
        {
            return false;
        }

        int seatCount = ResolvePlayByPostSeatMetadataSeatCount(
            save.seatCount,
            save.seats,
            localSeatIndex + 1);
        int activeSeatIndex = ResolveCurrentTurnSeatIndex(save, seatCount);
        if (activeSeatIndex != localSeatIndex)
        {
            return false;
        }

        List<PlayByPostSeatMetadata> normalizedSeats = BuildNormalizedPlayByPostSeatMetadata(
            save.seats,
            seatCount,
            save.playerOneTypedDisplayName,
            save.playerTwoTypedDisplayName);
        if (localSeatIndex >= normalizedSeats.Count)
        {
            return false;
        }

        PlayByPostSeatMetadata localSeat = normalizedSeats[localSeatIndex];
        string normalizedLocalState = PlayByPostSeatUtility.NormalizeSeatState(localSeat.state);
        if (string.Equals(normalizedLocalState, PlayByPostSeatUtility.SeatStateResigned, System.StringComparison.Ordinal) ||
            string.Equals(normalizedLocalState, PlayByPostSeatUtility.SeatStateEliminated, System.StringComparison.Ordinal))
        {
            return false;
        }

        localSeat.state = PlayByPostSeatUtility.SeatStateResigned;

        string normalizedClaimedPlayerId = NormalizeClaimedPlayerIdMetadataValue(claimedPlayerId);
        if (!string.IsNullOrWhiteSpace(normalizedClaimedPlayerId))
        {
            localSeat.claimedPlayerId = normalizedClaimedPlayerId;
        }

        string normalizedTypedDisplayName = NormalizeTypedDisplayNameMetadataValue(typedDisplayName);
        if (!string.IsNullOrWhiteSpace(normalizedTypedDisplayName))
        {
            localSeat.typedDisplayName = normalizedTypedDisplayName;
        }

        ResolveAdvancedPlayByPostTurnState(
            save,
            normalizedSeats,
            seatCount,
            activeSeatIndex,
            out int nextSeatIndex,
            out int nextTurnNumber,
            out _,
            out int eligibleSeatCount);

        save.seatCount = seatCount;
        save.seats = normalizedSeats;
        save.playerOneTypedDisplayName = normalizedSeats.Count > 0
            ? NormalizeTypedDisplayNameMetadataValue(normalizedSeats[0].typedDisplayName)
            : null;
        save.playerTwoTypedDisplayName = normalizedSeats.Count > 1
            ? NormalizeTypedDisplayNameMetadataValue(normalizedSeats[1].typedDisplayName)
            : null;
        save.currentTurnSeatIndex = nextSeatIndex;
        save.isPlayerTurn = nextSeatIndex == 0;
        save.turnNumber = nextTurnNumber;
        save.gameOver = eligibleSeatCount <= 1;
        save.hasWinnerSeatIndex = false;
        save.winnerSeatIndex = -1;
        if (save.gameOver)
        {
            if (!TryResolvePbpWinnerSeatFromEligibleSeats(normalizedSeats, seatCount, out int winnerSeatIndex))
            {
                return false;
            }

            save.hasWinnerSeatIndex = true;
            save.winnerSeatIndex = winnerSeatIndex;
        }
        save.transportSeq = ComputeTransportSeq(save.turnNumber, save.currentTurnSeatIndex, seatCount);

        resignedJson = JsonUtility.ToJson(save, false);
        exportTurnNumber = save.turnNumber;
        exportIsPlayerTurn = save.isPlayerTurn;
        exportCurrentTurnSeatIndex = save.currentTurnSeatIndex;
        exportTransportSeq = save.transportSeq;
        exportSeatCount = seatCount;
        exportGameOver = save.gameOver;
        return !string.IsNullOrWhiteSpace(resignedJson);
    }

    public static bool TryPatchClaimedPlayByPostSeatMetadataJson(
        string json,
        string expectedGameId,
        int claimedSeatIndex,
        string claimedPlayerId,
        string typedDisplayName,
        out string patchedJson)
    {
        patchedJson = json;
        if (string.IsNullOrWhiteSpace(json) ||
            claimedSeatIndex < 0 ||
            claimedSeatIndex >= PlayByPostSeatUtility.MaxSeatCount)
        {
            return false;
        }

        GameSave save;
        try
        {
            save = JsonUtility.FromJson<GameSave>(json);
        }
        catch
        {
            return false;
        }

        if (save == null ||
            !string.Equals(save.mode, GameMode.PlayByPost.ToString(), System.StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedGameId) &&
            !string.IsNullOrWhiteSpace(save.gameId) &&
            !string.Equals(save.gameId, expectedGameId, System.StringComparison.Ordinal))
        {
            return false;
        }

        int seatCount = ResolvePlayByPostSeatMetadataSeatCount(
            save.seatCount,
            save.seats,
            claimedSeatIndex + 1);
        List<PlayByPostSeatMetadata> normalizedSeats = BuildNormalizedPlayByPostSeatMetadata(
            save.seats,
            seatCount,
            save.playerOneTypedDisplayName,
            save.playerTwoTypedDisplayName);
        if (claimedSeatIndex >= normalizedSeats.Count)
        {
            return false;
        }

        PlayByPostSeatMetadata claimedSeat = normalizedSeats[claimedSeatIndex];
        string normalizedState = PlayByPostSeatUtility.NormalizeSeatState(claimedSeat.state);
        if (!string.Equals(normalizedState, PlayByPostSeatUtility.SeatStateResigned, System.StringComparison.Ordinal) &&
            !string.Equals(normalizedState, PlayByPostSeatUtility.SeatStateEliminated, System.StringComparison.Ordinal))
        {
            claimedSeat.state = PlayByPostSeatUtility.SeatStateActive;
        }

        string normalizedClaimedPlayerId = NormalizeClaimedPlayerIdMetadataValue(claimedPlayerId);
        if (!string.IsNullOrWhiteSpace(normalizedClaimedPlayerId))
        {
            claimedSeat.claimedPlayerId = normalizedClaimedPlayerId;
        }

        string normalizedTypedDisplayName = NormalizeTypedDisplayNameMetadataValue(typedDisplayName);
        if (!string.IsNullOrWhiteSpace(normalizedTypedDisplayName))
        {
            claimedSeat.typedDisplayName = normalizedTypedDisplayName;
        }

        save.seatCount = seatCount;
        save.seats = normalizedSeats;
        save.playerOneTypedDisplayName = normalizedSeats.Count > 0
            ? NormalizeTypedDisplayNameMetadataValue(normalizedSeats[0].typedDisplayName)
            : null;
        save.playerTwoTypedDisplayName = normalizedSeats.Count > 1
            ? NormalizeTypedDisplayNameMetadataValue(normalizedSeats[1].typedDisplayName)
            : null;

        patchedJson = JsonUtility.ToJson(save, false);
        return !string.IsNullOrWhiteSpace(patchedJson);
    }

    public static bool TryCanonicalizePlayByPostSnapshotJson(
        string json,
        string expectedGameId,
        out string canonicalJson,
        out int canonicalTurnNumber,
        out bool canonicalIsPlayerTurn,
        out int canonicalCurrentTurnSeatIndex,
        out int canonicalTransportSeq,
        out int canonicalSeatCount,
        out bool canonicalGameOver)
    {
        canonicalJson = json;
        canonicalTurnNumber = 0;
        canonicalIsPlayerTurn = false;
        canonicalCurrentTurnSeatIndex = 0;
        canonicalTransportSeq = 0;
        canonicalSeatCount = PlayByPostSeatUtility.MinSeatCount;
        canonicalGameOver = false;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        GameSave save;
        try
        {
            save = JsonUtility.FromJson<GameSave>(json);
        }
        catch
        {
            return false;
        }

        if (save == null ||
            !string.Equals(save.mode, GameMode.PlayByPost.ToString(), System.StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedGameId) &&
            !string.IsNullOrWhiteSpace(save.gameId) &&
            !string.Equals(save.gameId, expectedGameId, System.StringComparison.Ordinal))
        {
            return false;
        }

        int seatCount = ResolvePlayByPostSeatMetadataSeatCount(
            save.seatCount,
            save.seats);
        List<PlayByPostSeatMetadata> normalizedSeats = BuildNormalizedPlayByPostSeatMetadata(
            save.seats,
            seatCount,
            save.playerOneTypedDisplayName,
            save.playerTwoTypedDisplayName);
        int currentTurnSeatIndex = ResolveCurrentTurnSeatIndex(save, seatCount);

        save.seatCount = seatCount;
        save.seats = normalizedSeats;
        save.playerOneTypedDisplayName = normalizedSeats.Count > 0
            ? NormalizeTypedDisplayNameMetadataValue(normalizedSeats[0].typedDisplayName)
            : null;
        save.playerTwoTypedDisplayName = normalizedSeats.Count > 1
            ? NormalizeTypedDisplayNameMetadataValue(normalizedSeats[1].typedDisplayName)
            : null;
        save.currentTurnSeatIndex = currentTurnSeatIndex;
        save.isPlayerTurn = currentTurnSeatIndex == 0;
        save.transportSeq = ComputeTransportSeq(save.turnNumber, currentTurnSeatIndex, seatCount);
        if (save.hasWinnerSeatIndex)
        {
            save.winnerSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(save.winnerSeatIndex, seatCount);
        }

        canonicalJson = JsonUtility.ToJson(save, false);
        canonicalTurnNumber = save.turnNumber;
        canonicalIsPlayerTurn = save.isPlayerTurn;
        canonicalCurrentTurnSeatIndex = save.currentTurnSeatIndex;
        canonicalTransportSeq = save.transportSeq;
        canonicalSeatCount = save.seatCount;
        canonicalGameOver = save.gameOver;
        return !string.IsNullOrWhiteSpace(canonicalJson);
    }

    public static bool TryIngestFetchedPlayByPostSnapshotJson(
        string json,
        string expectedGameId,
        int fallbackTransportSeq,
        int fallbackSeatCount,
        string historySource,
        out int canonicalTurnNumber,
        out bool canonicalIsPlayerTurn,
        out int canonicalCurrentTurnSeatIndex,
        out int canonicalTransportSeq,
        out int canonicalSeatCount,
        out bool canonicalGameOver)
    {
        canonicalTurnNumber = 0;
        canonicalIsPlayerTurn = false;
        canonicalCurrentTurnSeatIndex = 0;
        canonicalTransportSeq = 0;
        canonicalSeatCount = PlayByPostSeatUtility.MinSeatCount;
        canonicalGameOver = false;

        if (!TryCanonicalizePlayByPostSnapshotJson(
                json,
                expectedGameId,
                out string canonicalJson,
                out canonicalTurnNumber,
                out canonicalIsPlayerTurn,
                out canonicalCurrentTurnSeatIndex,
                out canonicalTransportSeq,
                out canonicalSeatCount,
                out canonicalGameOver))
        {
            return false;
        }

        string gameId = expectedGameId;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            try
            {
                GameSave canonicalSave = JsonUtility.FromJson<GameSave>(canonicalJson);
                gameId = canonicalSave != null ? canonicalSave.gameId : null;
            }
            catch
            {
                gameId = null;
            }
        }

        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        string snapshotPath = GetPbpPerGameSavePathStatic(gameId);
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return false;
        }

        try
        {
            string snapshotDirectory = Path.GetDirectoryName(snapshotPath);
            if (!string.IsNullOrWhiteSpace(snapshotDirectory))
            {
                Directory.CreateDirectory(snapshotDirectory);
            }

            File.WriteAllText(snapshotPath, canonicalJson);

            if (Instance != null)
            {
                Instance.TryCaptureSnapshotHistoryCopy(
                    gameId,
                    canonicalJson,
                    canonicalTurnNumber,
                    canonicalIsPlayerTurn,
                    snapshotPath,
                    string.IsNullOrWhiteSpace(historySource) ? "snapshot_ingest" : historySource);
            }
        }
        catch (System.Exception)
        {
            return false;
        }

        SaveManifestService.RecordLoadApplied(
            gameId,
            GameMode.PlayByPost,
            canonicalGameOver,
            lastKnownRoundTurn: canonicalTurnNumber,
            lastKnownIsPlayerTurn: canonicalIsPlayerTurn,
            lastKnownCurrentTurnSeatIndex: canonicalCurrentTurnSeatIndex,
            lastKnownTransportSeq: canonicalTransportSeq > 0 ? canonicalTransportSeq : fallbackTransportSeq,
            lastKnownSeatCount: canonicalSeatCount > 0 ? canonicalSeatCount : fallbackSeatCount);
        return true;
    }
}
