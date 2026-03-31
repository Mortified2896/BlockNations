// CHANGELOG (PBp Auto Sync):
// - Added transport abstraction + provider wiring for automatic Play-by-Post turn sync.
// - Extracted `ApplyLoadedSave` and added `LoadFromJsonString` for transport/clipboard loads.
// - Added PBp auto-submit + polling loop and `PlayByPostSyncNow` (manual fetch button hook).
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class TurnManager : MonoBehaviour
{
    public enum GameMode
    {
        None,
        VsAI,
        PlayByPost
    }

    public enum AIDifficulty
    {
        None,
        Level1,
        Level2,
        Level3,
        Unfair
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
    public int turnNumber = 1;
    public bool gameOver = false;

    [Header("Economy")]
    // Base starting gold; income from cities adds on top at game start.
    public int startingGold = 2;
    public int playerGold = 2;
    public int aiGold = 0;
    public int goldPerCity = 1;
    public int warriorCost => GetRecruitCost(UnitRegistry.WarriorTypeId);

    [Header("AI Settings")]
    public float aiTurnDelay = 1f; // seconds the AI "thinks" before ending its turn
    public AIDifficulty aiDifficulty = AIDifficulty.Level1;

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

    [Header("Audio")]
    public bool playMusicOnStart = true;
    public AudioClip gameplayMusic;

    [Header("Saving")]
    public bool autoSaveEnabled = true;
    public string autoSaveFileName = "save.json";
    public bool playByPostExportPretty = true;

    [Header("AI Debug")]
    public bool disableAI = false;

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
    private int lastAppliedTurnNumberForPolling = 0;
    private bool isPlayByPostFetchInProgress = false;
    private bool playByPostLastFetchWasNoTurn = false;
    private float playByPostLastNoTurnLogTime = -999f;
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
    private const float PlayByPostNoTurnLogCooldownSeconds = 5f;
    private const string PlayByPostGameIdKey = "pbp_gameId";
    private const string PlayByPostForceNewKey = "pbp_forceNew";
    private const string PlayByPostPendingNewGameIdKey = "pbp_pendingNewGameId";
    private const string PlayByPostPrimarySaveFileName = "save.json";
    private const string SinglePlayerPrimarySaveFileName = "save_sp.json";
    private const string PlayByPostPerGameSaveFolderName = "pbp";
    private const string PlayByPostPerGameSavePrefix = "pbp_";
    private const string ReturnToMultiplayerPaneKey = "ui_returnToMultiplayerPane";
    private const string MainMenuSceneName = "MainMenu";
    private const string DefaultGameOverMessage = "Game Over";
    private const string DefaultGameOverPrimaryButtonLabel = "Play Again";
    private const int SupportedPbpProtocolVersion = 3;
    public static int PbpProtocolVersion => SupportedPbpProtocolVersion;

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
        BackAndDelete
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
    private string gameOverUiMessage = string.Empty;
    private string gameOverUiPrimaryButtonLabel = DefaultGameOverPrimaryButtonLabel;
    private bool gameOverUiPrimaryButtonInteractable = true;
    [System.Serializable]
    private class SavedCity
    {
        public int x;
        public int y;
        public bool isPlayerOwned;
        public bool hasRecruitedThisTurn;
    }

    [System.Serializable]
    private class SavedUnit
    {
        public string unitTypeId;
        public bool isPlayerOwned;
        public float x;
        public float y;
        public float z;
        public int currentHealth;
        public int movesUsedThisTurn;
        public bool hasAttackedThisTurn;
    }

    [System.Serializable]
    private class SavedTile
    {
        public int x;
        public int y;
        public bool playerSeen;
        public bool opponentSeen;
    }

    [System.Serializable]
    private class GameSave
    {
        public string version = "2";
        public int protocolVersion;
        public string gameId;
        public string mode;
        public string aiDifficulty;
        public bool isPlayerTurn;
        public int turnNumber;
        public int playerGold;
        public int aiGold;
        public bool gameOver;
        public int visibilityRadius;
        public string playerOneTypedDisplayName;
        public string playerTwoTypedDisplayName;
        public List<SavedCity> cities = new List<SavedCity>();
        public List<SavedUnit> units = new List<SavedUnit>();
        public List<SavedTile> tiles = new List<SavedTile>();
    }

    // Stable id for the current campaign/save chain so exports can be shared
    private string currentGameId;
    private string typedDisplayMetadataGameId;
    private string knownPlayerOneTypedDisplayName;
    private string knownPlayerTwoTypedDisplayName;
    private string cachedGameIdRaw;
    private string cachedGameIdHash;
    public event System.Action<bool, string> PlayByPostSubmitResult;
    public event System.Action<bool, string> PlayByPostFetchResult;
    public bool IsGameOverUiVisible => gameOver;
    public string GameOverUiMessage =>
        string.IsNullOrWhiteSpace(gameOverUiMessage) ? DefaultGameOverMessage : gameOverUiMessage;
    public string GameOverUiPrimaryButtonLabel =>
        string.IsNullOrWhiteSpace(gameOverUiPrimaryButtonLabel) ? DefaultGameOverPrimaryButtonLabel : gameOverUiPrimaryButtonLabel;
    public bool GameOverUiPrimaryButtonInteractable => gameOverUiPrimaryButtonInteractable;
    public bool IsPbpEndgameMenuExitBlocked =>
        currentMode == GameMode.PlayByPost &&
        gameOver &&
        pbpEndgameLocalWinner &&
        pbpEndgameSubmitPending;

    public bool IsHumanTurn()
    {
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

    public GameObject GetUnitPrefabForType(string unitTypeId)
    {
        if (!UnitRegistry.TryGetDefinition(unitTypeId, out UnitDefinition definition))
        {
            return null;
        }

        string resolvedPrefabTypeId = UnitRegistry.NormalizeTypeId(definition.PrefabTypeId);
        if (resolvedPrefabTypeId == UnitRegistry.WarriorTypeId)
        {
            if (unitPrefab != null)
            {
                return unitPrefab;
            }

            City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
            foreach (City city in cities)
            {
                if (city != null && city.warriorPrefab != null)
                {
                    return city.warriorPrefab;
                }
            }
        }

        return null;
    }

    public GameObject InstantiateConfiguredUnit(
        string unitTypeId,
        GameObject prefab,
        Vector3 position,
        bool isPlayerOwned,
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

            unit.isPlayerOwned = isPlayerOwned;
            unit.currentCity = currentCity;
            if (resetTurnState)
            {
                unit.ResetMovementForTurn();
            }

            bool isActiveTurn = IsCurrentSideOwner(isPlayerOwned);
            unit.UpdateMoveOutline(resetTurnState && isActiveTurn);
        }

        OwnedSprite owned = spawnedObject.GetComponent<OwnedSprite>();
        if (owned != null)
        {
            owned.SetOwner(isPlayerOwned);
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
            seat = storedSeat <= 0 ? 0 : 1;
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
        }

        save.playerOneTypedDisplayName = knownPlayerOneTypedDisplayName;
        save.playerTwoTypedDisplayName = knownPlayerTwoTypedDisplayName;

        if (!TryGetLocalSeatIndexForPbp(currentGameId, out int localSeat))
        {
            return;
        }

        string localTypedDisplayName = LocalPlayerProfileStore.NormalizeTypedDisplayName(
            LocalPlayerProfileStore.GetOrCreateProfile().TypedDisplayName);
        localTypedDisplayName = string.IsNullOrEmpty(localTypedDisplayName) ? null : localTypedDisplayName;

        if (localSeat == 0)
        {
            save.playerOneTypedDisplayName = localTypedDisplayName;
            knownPlayerOneTypedDisplayName = localTypedDisplayName;
            return;
        }

        save.playerTwoTypedDisplayName = localTypedDisplayName;
        knownPlayerTwoTypedDisplayName = localTypedDisplayName;
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
            return;
        }

        typedDisplayMetadataGameId = currentGameId;
        knownPlayerOneTypedDisplayName = NormalizeTypedDisplayNameMetadataValue(save.playerOneTypedDisplayName);
        knownPlayerTwoTypedDisplayName = NormalizeTypedDisplayNameMetadataValue(save.playerTwoTypedDisplayName);
    }

    private static string NormalizeTypedDisplayNameMetadataValue(string value)
    {
        string normalized = LocalPlayerProfileStore.NormalizeTypedDisplayName(value);
        return string.IsNullOrEmpty(normalized) ? null : normalized;
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
        if (currentMode == GameMode.PlayByPost)
        {
            if (string.IsNullOrWhiteSpace(currentGameId))
            {
                // Startup transient: gameId not assigned yet; seat lookup not possible;
                // defer correctness until SetCurrentGameId.
                return true;
            }

            if (TryGetLocalSeatIndexForPbp(currentGameId, out int localSeat))
            {
                return localSeat == 0;
            }

            // Visual-only fallback when seat data is unavailable; control gating
            // remains locked by EnsurePlayByPostControlReadiness/CanLocalPlayerIssueCommands.
            return true;
        }

        // VsAI mode currently treats the local viewer as the player-owned side.
        // If future modes support non-player-owned local viewpoints, update this helper.
        return true;
    }

    public bool IsCurrentSideOwner(bool isPlayerOwned)
    {
        if (currentMode == GameMode.PlayByPost)
        {
            bool me = GetLocalIsPlayerOneForGame(currentGameId, out bool hasSeat, out _);
            if (!hasSeat)
                return false;

            return (isPlayerTurn == me) && (isPlayerOwned == me);
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

        bool localIsPlayerOne = seat == 0;
        return isPlayerTurn == localIsPlayerOne;
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
        string unitIdForLog = unit != null ? unit.GetInstanceID().ToString() : "<none>";
        string unitOwnerForLog = unit != null ? unit.isPlayerOwned.ToString() : "<none>";
        Debug.Log(
            $"[PBpSelectionGate] gate={gate} reason={reasonForLog} currentGameId={gameIdForLog} isPlayByPostFetchInProgress={isPlayByPostFetchInProgress} isPlayByPostWaitingForExport={isPlayByPostWaitingForExport} pbpControlReadinessReady={pbpControlReadinessReady} pointerOverUi={pointerOverUi} unitName={unitNameForLog} unitId={unitIdForLog} unitIsPlayerOwned={unitOwnerForLog}");
    }
#endif

    public bool CanControlUnit(Unit unit)
    {
        if (unit == null || gameOver)
            return false;

        if (currentMode == GameMode.PlayByPost && !CanLocalPlayerIssueCommands())
            return false;

        return IsCurrentSideOwner(unit.isPlayerOwned);
    }

    public bool CanControlCity(City city)
    {
        if (city == null || gameOver)
            return false;

        if (currentMode == GameMode.PlayByPost && !CanLocalPlayerIssueCommands())
            return false;

        return IsCurrentSideOwner(city.isPlayerOwned);
    }

    public string GetCurrentSideName()
    {
        if (currentMode == GameMode.PlayByPost)
        {
            return isPlayerTurn ? "Player 1" : "Player 2";
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
        playByPostLastFetchWasNoTurn = false;
        pbpControlReadinessReady = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        lastPbpControlReadinessBlockKey = null;
        lastPbpSeatAdoptionLogKey = null;
        lastPbpInputDeniedLogTime = -999f;
        lastPbpSelectionGateLogTime = -999f;
#endif
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
    }

    private void ResetGameOverUiState()
    {
        gameOverUiMessage = string.Empty;
        gameOverUiPrimaryButtonLabel = DefaultGameOverPrimaryButtonLabel;
        gameOverUiPrimaryButtonInteractable = true;
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
        if (currentMode == GameMode.PlayByPost && gameOver)
        {
            if (pbpEndgamePrimaryAction == PbpEndgamePrimaryAction.RetrySubmit)
            {
                TryStartPbpEndgameAutoSubmit();
                return;
            }

            if (pbpEndgamePrimaryAction == PbpEndgamePrimaryAction.BackAndDelete)
            {
                ReturnToMultiplayerAndDeleteLocalPbpCopy();
                return;
            }

            return;
        }

        // Preserve the mode (VsAI / PlayByPost) for the next game.
        GameModeSelection.SetPendingMode(currentMode);

        Time.timeScale = 1f;
        var currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }

    private void SetGameOverPrimaryButtonState(string label, bool interactable, PbpEndgamePrimaryAction action)
    {
        pbpEndgamePrimaryAction = action;
        string resolvedLabel = string.IsNullOrWhiteSpace(label) ? DefaultGameOverPrimaryButtonLabel : label;
        gameOverUiPrimaryButtonLabel = resolvedLabel;
        gameOverUiPrimaryButtonInteractable = interactable;
    }

    private void ShowGameOverPopup(string message, bool writeLog = true)
    {
        SetGameOverUiMessage(message);
        if (currentMode != GameMode.PlayByPost)
        {
            gameOverUiPrimaryButtonLabel = DefaultGameOverPrimaryButtonLabel;
            gameOverUiPrimaryButtonInteractable = true;
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

    private bool TryComputePbpWinnerSeatFromBoard(out int winnerSeatIndex)
    {
        winnerSeatIndex = 0;
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        if (cities == null || cities.Length == 0)
            return false;

        // Preferred PBp endgame inference for loaded snapshots:
        // if a city is occupied by an opposite-owner unit, treat that
        // unit owner as the capturing winner side.
        bool foundCaptureMarker = false;
        bool captureWinnerIsPlayerOwned = false;
        for (int i = 0; i < cities.Length; i++)
        {
            City city = cities[i];
            if (city == null)
                continue;

            Unit occupyingUnit = GridUtils.GetUnitAtPosition(city.transform.position);
            if (occupyingUnit == null || occupyingUnit.isPlayerOwned == city.isPlayerOwned)
                continue;

            if (!foundCaptureMarker)
            {
                foundCaptureMarker = true;
                captureWinnerIsPlayerOwned = occupyingUnit.isPlayerOwned;
                continue;
            }

            if (occupyingUnit.isPlayerOwned != captureWinnerIsPlayerOwned)
                return false;
        }

        if (foundCaptureMarker)
        {
            winnerSeatIndex = captureWinnerIsPlayerOwned ? 0 : 1;
            return true;
        }

        bool owner = cities[0].isPlayerOwned;
        for (int i = 1; i < cities.Length; i++)
        {
            if (cities[i] == null)
                continue;

            if (cities[i].isPlayerOwned != owner)
                return false;
        }

        winnerSeatIndex = owner ? 0 : 1;
        return true;
    }

    private void ConfigurePbpEndgameFromCapture(bool didLocalWin)
    {
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
            return;
        }

        SetGameOverPrimaryButtonState("Back to Multiplayer & Delete local copy", true, PbpEndgamePrimaryAction.BackAndDelete);
    }

    private void ConfigurePbpEndgameFromLoadedState()
    {
        if (!TryComputePbpWinnerSeatFromBoard(out int winnerSeatIndex))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("PBp gameOver load could not derive a single objective winner seat from board ownership.");
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
            SetGameOverPrimaryButtonState("Back to Multiplayer & Delete local copy", true, PbpEndgamePrimaryAction.BackAndDelete);
            return;
        }

        if (!TryComputePbpLocalResult(winnerSeatIndex, out bool didLocalWin, out _))
        {
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
            SetGameOverPrimaryButtonState("Back to Multiplayer & Delete local copy", true, PbpEndgamePrimaryAction.BackAndDelete);
            return;
        }

        string message = didLocalWin ? "You won!" : "You lost!";
        ShowGameOverPopup(message, writeLog: false);

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
            return;
        }

        SetGameOverPrimaryButtonState("Back to Multiplayer & Delete local copy", true, PbpEndgamePrimaryAction.BackAndDelete);
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
            SetGameOverPrimaryButtonState("Back to Multiplayer & Delete local copy", true, PbpEndgamePrimaryAction.BackAndDelete);
            return;
        }

        SetGameOverPrimaryButtonState("Retry submit", true, PbpEndgamePrimaryAction.RetrySubmit);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning($"PBp endgame submit failed (err={(err ?? "<null>")}).");
#endif
    }

    private void ReturnToMultiplayerAndDeleteLocalPbpCopy()
    {
        string gameId = GetPbpGameIdFromPrefsOrCurrent();
        DeleteLocalPbpGameCopy(gameId);

        PlayerPrefs.SetInt(ReturnToMultiplayerPaneKey, 1);
        PlayerPrefs.Save();

        Time.timeScale = 1f;
        SceneManager.LoadScene(MainMenuSceneName);
    }

    private void DeleteLocalPbpGameCopy(string gameId)
    {
        MainMenuController.DeleteLocalPlayByPostGameData(gameId, clearActiveGameSelection: true);
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

        if (SoundManager.Instance != null)
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
                    snapshotGameId: exportSave.gameId);
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
        int transportSeq = turnNumber * 2 + (isPlayerTurn ? 0 : 1);
        return $"mode={currentMode},gameId={gameIdForLog},roundTurn={turnNumber},isPlayerTurn={isPlayerTurn},isWaitingForExport={isPlayByPostWaitingForExport},transportSeq={transportSeq},lastAppliedTransportSeq={lastAppliedTurnNumberForPolling}";
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
            Debug.Log($"PBp submit ok via {turnTransport.TransportName} (gameId={currentGameId}, turn={transportSeq}).");
        }
        else
        {
            Debug.LogWarning($"PBp submit failed via {turnTransport.TransportName} (gameId={currentGameId}, turn={transportSeq}): {submitError}");
        }

        TryNotifyPlayByPostSubmitResult(submitOk, submitError);
        if (submitOk)
        {
            // Re-save on successful submit so disk state always matches the submitted turn.
            SavePlayByPostPerGameSnapshot(
                snapshotJson: exportJson,
                snapshotRoundTurn: exportTurnNumber,
                snapshotIsPlayerTurn: exportIsPlayerTurn,
                snapshotGameId: exportGameId);
            SaveManifestService.RecordPlayByPostExport(
                currentGameId,
                turnTransport != null ? turnTransport.TransportName : null,
                lastKnownRoundTurn: exportTurnNumber,
                lastKnownIsPlayerTurn: exportIsPlayerTurn);
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

        SavePlayByPostPerGameSnapshot();
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

        bool loaded = LoadFromJsonString(json);
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

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayTurnStart();
        }

        // Simulate thinking time
        yield return new WaitForSeconds(aiTurnDelay);

        // Collect AI income at the start of its turn
        CollectAIGold();

        // AI actions: recruit and move units
        ResetRecruitmentForAICities();
        if (!disableAI)
        {
            RunAI();
        }

        if (gameOver)
            yield break;

        // Back to player
        turnNumber++;
        BeginPlayerTurn();
    }

    void BeginPlayerTurn()
    {
        if (gameOver)
            return;

        autoEndTurnDisabledLoggedThisTurn = false;
        isPlayerTurn = true;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayTurnStart();
        }

        // Allow cities and units to act again
        ResetRecruitmentForPlayerCities();
        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.ResetMovementForSide(true, IsCurrentSideOwner(true));
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

        CollectPlayerIncome();
        RecalculatePlayerVisibility();

        ScheduleAutoEndTurnCheck();

        if (currentMode == GameMode.VsAI)
        {
            AutoSaveIfEnabled();
        }
    }

    System.Collections.IEnumerator StartupSequence()
    {
        if (TryConsumeForceNewPlayByPostRequest(out string forcedNewGameId))
        {
            pbpCreatorBootstrapGameId = forcedNewGameId;
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
        isPlayerTurn = true;
        playerGold = startingGold;
        aiGold = startingGold;
        aiDifficulty = AIDifficulty.Level1;

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

        if (currentMode == GameMode.PlayByPost)
        {
            InitializePlayByPostSession();
        }
        else if (string.IsNullOrEmpty(currentGameId))
        {
            SetCurrentGameId(System.Guid.NewGuid().ToString());
        }

        if (AIDifficultySelection.TryConsume(out AIDifficulty pendingDifficulty))
        {
            aiDifficulty = pendingDifficulty;
        }

        ResetRecruitmentForPlayerCities();
        // Give income only to the side whose turn it is.
        // The other side receives income at the start of
        // its first turn (via Begin*Turn / AITurn).
        CollectPlayerIncome();
        RecalculatePlayerVisibility();

        ScheduleAutoEndTurnCheck();

        RefreshEndTurnButtonInteractable(force: true);
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
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (UnitDefinition unitDefinition in UnitRegistry.AllDefinitions)
        {
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

        // 2) Unit movement / attacks (adjacent).
        float tileSize = gridManager != null ? Mathf.Max(0.01f, gridManager.tileSize) : 1f;

        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            if (unit == null || !unit.isPlayerOwned)
                continue;

            if (HasAnyLegalAdjacentAction(unit, tileSize))
                return true;
        }

        return false;
    }

    private bool HasAnyLegalAdjacentAction(Unit unit, float tileSize)
    {
        if (unit == null)
            return false;

        bool canMove = unit.CanMoveThisTurn();
        bool canAttack = !unit.hasAttackedThisTurn;

        if (!canMove && !canAttack)
            return false;

        Vector3 from = unit.transform.position;
        from.z = 0f;

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
                if (occupant != null)
                {
                    if (canAttack && occupant.isPlayerOwned != unit.isPlayerOwned)
                        return true;

                    continue;
                }

                if (canMove)
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
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            if (city.isPlayerOwned)
            {
                city.hasRecruitedThisTurn = false;
            }
        }
    }

    void ResetRecruitmentForAICities()
    {
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            if (!city.isPlayerOwned)
            {
                city.hasRecruitedThisTurn = false;
            }
        }
    }

    void RunAI()
    {
        // 1) Recruit from each AI city (one unit per city per AI turn, if the city is empty)
        City[] allCities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        City primaryAICity = null;
        foreach (City city in allCities)
        {
            if (!city.isPlayerOwned && city.CanRecruit())
            {
                city.SpawnWarrior();
            }

            if (!city.isPlayerOwned && primaryAICity == null)
            {
                primaryAICity = city;
            }
        }

        // 2) Move AI units toward the nearest player unit or city.
        bool aiHasPerfectInfo = aiDifficulty == AIDifficulty.Unfair;
        Unit[] allUnits = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);

        HashSet<TileVisibility> aiVisibleTiles = aiHasPerfectInfo ? null : ComputeVisibilityForSide(false);

        // Collect player targets: visible units + known player cities.
        List<Vector3> playerTargets = new List<Vector3>();
        List<Vector3> playerCityPositions = new List<Vector3>();
        List<TileVisibility> playerUnitTiles = new List<TileVisibility>();
        bool anyVisiblePlayerUnit = false;
        bool enemyNearAICity = false;

        foreach (Unit unit in allUnits)
        {
            if (!unit.isPlayerOwned)
                continue;

            // Unfair AI: always knows all player unit positions. Otherwise, respect visibility.
            bool unitIsVisible = aiHasPerfectInfo;

            // If we could not determine visibility, fall back to the old behavior.
            if (!unitIsVisible && (aiVisibleTiles == null || aiVisibleTiles.Count == 0 || gridManager == null))
            {
                playerTargets.Add(unit.transform.position);
                anyVisiblePlayerUnit = true;
                continue;
            }

            if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile))
            {
                if (aiHasPerfectInfo || aiVisibleTiles.Contains(tile))
                {
                    playerTargets.Add(unit.transform.position);
                    playerUnitTiles.Add(tile);
                    anyVisiblePlayerUnit = true;

                    // Track whether any enemy unit is near the AI city.
                    if (primaryAICity != null)
                    {
                        int dxCity = Mathf.Abs(tile.gridX - primaryAICity.x);
                        int dyCity = Mathf.Abs(tile.gridY - primaryAICity.y);
                        int distToAICityTilesForEnemy = Mathf.Max(dxCity, dyCity);
                        if (distToAICityTilesForEnemy <= 2)
                        {
                            enemyNearAICity = true;
                        }
                    }
                }
            }
        }

        foreach (City city in allCities)
        {
            if (!city.isPlayerOwned)
                continue;

            // Cities are at fixed positions in this map, so
            // allow the AI to always know their locations.
            Vector3 cityPos = city.transform.position;
            playerTargets.Add(cityPos);
            playerCityPositions.Add(cityPos);
        }

        if (playerTargets.Count == 0)
        {
            // Nothing visible to move toward this turn (AI does not cheat).
            return;
        }

        // Determine grid step size from the GridManager (fallback to 1)
        float stepSize = gridManager != null ? Mathf.Max(0.01f, gridManager.tileSize) : 1f;

        // For threat checks: a player can move one tile then attack adjacent, so staying
        // beyond Chebyshev distance 2 avoids being attacked next turn by a fresh unit.
        const int playerThreatRadiusTiles = 2;

        // Level 2 behavior: if no enemy units are currently visible, units that
        // are closest to the enemy city have a small chance to hold position
        // instead of always advancing, to make behavior less predictable.
        bool applyLevel2HoldBehavior = (aiDifficulty == AIDifficulty.Level2) &&
                                       !anyVisiblePlayerUnit &&
                                       playerCityPositions.Count > 0;

        // Unfair skips the Level 3 defender anchor logic since it has perfect info and prefers full offense.
        bool applyLevel3DefenderBehavior = (aiDifficulty == AIDifficulty.Level3) &&
                                           (primaryAICity != null);

        // For Level 2 we compute distances in grid coordinates rather than world
        // space so the logic is stable regardless of tile size.
        Dictionary<Unit, int> distToEnemyCityTiles = null;
        int nearestEnemyCityDistTiles = int.MaxValue;

        Dictionary<Unit, int> distToAICityTiles = null;
        int nearestAICityDistTiles = int.MaxValue;
        Unit defenderCandidate = null;

        int GetThreatDistanceTiles(Vector3 pos)
        {
            if (gridManager == null || playerUnitTiles.Count == 0)
                return int.MaxValue;

            if (!gridManager.TryGetTileAtWorldPosition(pos, out TileVisibility posTile))
                return int.MaxValue;

            int minDist = int.MaxValue;
            foreach (var enemyTile in playerUnitTiles)
            {
                int dx = Mathf.Abs(enemyTile.gridX - posTile.gridX);
                int dy = Mathf.Abs(enemyTile.gridY - posTile.gridY);
                int dist = Mathf.Max(dx, dy);
                if (dist < minDist)
                {
                    minDist = dist;
                }
            }
            return minDist;
        }

        Vector3 PredictOneStep(Vector3 from, Vector3 target)
        {
            Vector3 delta = target - from;
            delta.z = 0f;

            float stepX = 0f;
            float stepY = 0f;
            if (Mathf.Abs(delta.x) > 0.1f)
            {
                stepX = Mathf.Sign(delta.x) * stepSize;
            }
            if (Mathf.Abs(delta.y) > 0.1f)
            {
                stepY = Mathf.Sign(delta.y) * stepSize;
            }

            return new Vector3(from.x + stepX, from.y + stepY, from.z);
        }

        if (applyLevel2HoldBehavior || applyLevel3DefenderBehavior)
        {
            distToEnemyCityTiles = applyLevel2HoldBehavior ? new Dictionary<Unit, int>() : null;
            distToAICityTiles = applyLevel3DefenderBehavior ? new Dictionary<Unit, int>() : null;

            foreach (Unit unit in allUnits)
            {
                if (unit == null || unit.isPlayerOwned)
                    continue;

                if (!gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility originTile))
                    continue;

                if (applyLevel2HoldBehavior)
                {
                    int bestEnemyCityDistTiles = int.MaxValue;

                    foreach (City city in allCities)
                    {
                        if (!city.isPlayerOwned)
                            continue;

                        int dx = Mathf.Abs(originTile.gridX - city.x);
                        int dy = Mathf.Abs(originTile.gridY - city.y);
                        int distTiles = Mathf.Max(dx, dy); // Chebyshev distance (diagonal moves allowed)

                        if (distTiles < bestEnemyCityDistTiles)
                        {
                            bestEnemyCityDistTiles = distTiles;
                        }
                    }

                    if (bestEnemyCityDistTiles < int.MaxValue)
                    {
                        distToEnemyCityTiles[unit] = bestEnemyCityDistTiles;
                        if (bestEnemyCityDistTiles < nearestEnemyCityDistTiles)
                        {
                            nearestEnemyCityDistTiles = bestEnemyCityDistTiles;
                        }
                    }
                }

                if (applyLevel3DefenderBehavior && primaryAICity != null)
                {
                    int dxAi = Mathf.Abs(originTile.gridX - primaryAICity.x);
                    int dyAi = Mathf.Abs(originTile.gridY - primaryAICity.y);
                    int distToAiTiles = Mathf.Max(dxAi, dyAi);

                    distToAICityTiles[unit] = distToAiTiles;
                    if (distToAiTiles < nearestAICityDistTiles)
                    {
                        nearestAICityDistTiles = distToAiTiles;
                        defenderCandidate = unit;
                    }
                    else if (distToAiTiles == nearestAICityDistTiles && defenderCandidate == null)
                    {
                        defenderCandidate = unit;
                    }
                }
            }
        }

        // === Level 3: recruit vs recall defender when city is under visible threat ===
        if (applyLevel3DefenderBehavior && enemyNearAICity && primaryAICity != null)
        {
            // Enemy distance to AI city in tiles (Chebyshev)
            int enemyTurnsToCity = int.MaxValue;
            foreach (Unit unit in allUnits)
            {
                if (unit == null || !unit.isPlayerOwned)
                    continue;

                if (!gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility enemyTile))
                    continue;

                int dxE = Mathf.Abs(enemyTile.gridX - primaryAICity.x);
                int dyE = Mathf.Abs(enemyTile.gridY - primaryAICity.y);
                int distEnemy = Mathf.Max(dxE, dyE);
                if (distEnemy < enemyTurnsToCity)
                {
                    enemyTurnsToCity = distEnemy;
                }
            }

            // Closest AI unit distance to own city (from distToAICityTiles)
            int aiTurnsFromFront = nearestAICityDistTiles;

            // Estimate income per turn for AI from owned cities
            int aiIncomePerTurn = 0;
            foreach (City city in allCities)
            {
                if (!city.isPlayerOwned)
                {
                    aiIncomePerTurn += goldPerCity;
                }
            }

            int aiGoldNow = aiGold;
            int turnsUntilCanRecruit;
            int warriorRecruitCost = GetRecruitCost(UnitRegistry.WarriorTypeId);
            if (aiGoldNow >= warriorRecruitCost)
            {
                turnsUntilCanRecruit = 0;
            }
            else if (aiIncomePerTurn > 0)
            {
                turnsUntilCanRecruit = Mathf.CeilToInt((warriorRecruitCost - aiGoldNow) / (float)aiIncomePerTurn);
            }
            else
            {
                turnsUntilCanRecruit = int.MaxValue;
            }

            bool shouldRecruitDefender =
                enemyTurnsToCity < int.MaxValue &&
                turnsUntilCanRecruit <= enemyTurnsToCity;

            if (shouldRecruitDefender)
            {
                // Spawn defender at the AI city if the tile is free.
                Vector3 spawnPosition = primaryAICity.transform.position;
                GameObject warriorPrefab = GetUnitPrefabForType(UnitRegistry.WarriorTypeId);
                if (!GridUtils.IsTileOccupied(spawnPosition, null) && warriorPrefab != null)
                {
                    // TrySpendGold(false, ...) will also update AI gold and UI when appropriate.
                    if (TrySpendGold(false, warriorRecruitCost))
                    {
                        GameObject defender = InstantiateConfiguredUnit(
                            UnitRegistry.WarriorTypeId,
                            warriorPrefab,
                            spawnPosition,
                            isPlayerOwned: false,
                            currentCity: primaryAICity,
                            resetTurnState: true);
                        if (defender == null)
                        {
                            return;
                        }
                        primaryAICity.stationedUnit = defender;
                    }
                }
            }
        }

        foreach (Unit unit in allUnits)
        {
            if (unit.isPlayerOwned)
                continue;

            // Reset AI unit movement for this AI turn
            unit.ResetMovementForTurn();

            // Level 3: never idle on our own city tile; step off toward the defensive anchor
            // (or any free/attackable adjacent tile) before making other decisions.
            if (applyLevel3DefenderBehavior && primaryAICity != null && gridManager != null)
            {
                if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility unitTile) &&
                    unitTile.gridX == primaryAICity.x && unitTile.gridY == primaryAICity.y)
                {
                    Vector2Int[] offsets = new Vector2Int[]
                    {
                        new Vector2Int(1, 1),  // preferred anchor
                        new Vector2Int(1, 0),
                        new Vector2Int(0, 1),
                        new Vector2Int(-1, 0),
                        new Vector2Int(0, -1),
                        new Vector2Int(-1, -1),
                        new Vector2Int(1, -1),
                        new Vector2Int(-1, 1),
                    };

                    bool steppedOffCity = false;
                    foreach (var off in offsets)
                    {
                        int tx = primaryAICity.x + off.x;
                        int ty = primaryAICity.y + off.y;
                        if (!gridManager.TryGetTile(tx, ty, out TileVisibility anchorTile))
                            continue;

                        Vector3 targetPos = anchorTile.transform.position;
                        Unit occupant = GridUtils.GetUnitAtPosition(targetPos, unit);
                        // Skip if a friendly unit already occupies the tile; allow moving onto enemies.
                        if (occupant != null && occupant.isPlayerOwned == unit.isPlayerOwned)
                            continue;

                        MoveAIUnitOneStep(unit, targetPos, stepSize);
                        steppedOffCity = true;
                        break;
                    }

                    if (steppedOffCity)
                    {
                        // This unit already used its action to reposition off the city tile.
                        continue;
                    }
                }
            }

            // Optional Level 2 randomness: closest units to the enemy city
            // sometimes hold position when no enemies are currently visible.
            if (applyLevel2HoldBehavior &&
                distToEnemyCityTiles != null &&
                distToEnemyCityTiles.TryGetValue(unit, out int unitCityDistTiles))
            {
                // Treat as "closest group" if it has the minimum distance in tiles.
                if (unitCityDistTiles == nearestEnemyCityDistTiles)
                {
                    // Do not skip if this unit could capture the enemy city
                    // in a single tile move (axial or diagonal).
                    // Chebyshev distance 1 => within one move, 0 => already on the city.
                    if (unitCityDistTiles > 1 && Random.value < 0.5f)
                    {
                        // Skip moving this turn to keep behavior varied.
                        continue;
                    }
                }
            }

            // Optional Level 3 behavior: always try to keep the closest AI unit
            // near its own city as a defender when the city is not under visible
            // threat.
            if (applyLevel3DefenderBehavior &&
                !enemyNearAICity &&
                primaryAICity != null &&
                unit == defenderCandidate &&
                distToAICityTiles != null &&
                distToAICityTiles.TryGetValue(unit, out int unitDistToAiTiles))
            {
                // The closest AI unit to its own city acts as the defender.
                if (unitDistToAiTiles == nearestAICityDistTiles)
                {
                    // Prefer to hold one tile "behind" the city toward the corner,
                    // e.g., at (city.x + 1, city.y + 1) for the default top-right AI city.
                    if (gridManager != null)
                    {
                        int anchorX = primaryAICity.x + 1;
                        int anchorY = primaryAICity.y + 1;

                        if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility unitTile) &&
                            gridManager.TryGetTile(anchorX, anchorY, out TileVisibility anchorTile))
                        {
                            int distToAnchor = Mathf.Max(Mathf.Abs(unitTile.gridX - anchorX), Mathf.Abs(unitTile.gridY - anchorY));

                            // If already on the anchor tile, hold position.
                            if (distToAnchor == 0)
                            {
                                continue;
                            }

                            // Move toward the defensive anchor tile rather than the city center.
                            MoveAIUnitOneStep(unit, anchorTile.transform.position, stepSize);
                            continue;
                        }
                    }

                    // Fallback: if we cannot compute an anchor, keep the previous behavior
                    // of staying within 1 tile of the city.
                    if (unitDistToAiTiles <= 1)
                    {
                        continue;
                    }

                    MoveAIUnitOneStep(unit, primaryAICity.transform.position, stepSize);
                    continue;
                }
            }

            // Find nearest target
            Vector3 from = unit.transform.position;
            Vector3? bestTarget = null;
            float bestDistSq = float.MaxValue;

            foreach (Vector3 targetPos in playerTargets)
            {
                float dSq = (targetPos - from).sqrMagnitude;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestTarget = targetPos;
                }
            }

            if (bestTarget.HasValue)
            {
                Vector3 chosenTarget = bestTarget.Value;

                // Unfair AI: if the preferred step would end inside the player's move+attack radius,
                // try to choose a safer adjacent step that still heads toward the goal.
                // If no safe step exists, only retreat if we are not about to kill an adjacent player unit
                // by moving onto it.
                if (aiHasPerfectInfo && playerUnitTiles.Count > 0)
                {
                    Vector3 predictedPos = PredictOneStep(from, chosenTarget);
                    int threatDist = GetThreatDistanceTiles(predictedPos);

                    bool predictedKillsEnemy = false;
                    if (GridUtils.GetUnitAtPosition(predictedPos, unit) is Unit enemyAtPos &&
                        enemyAtPos.isPlayerOwned)
                    {
                        // If we step onto an enemy tile, treat as a kill attempt; allow it even in danger.
                        predictedKillsEnemy = true;
                    }

                    // Also allow entering danger if we will be adjacent and can attack after moving.
                    bool predictedCanAttackAdjacent = threatDist <= 1;

                    if (!predictedKillsEnemy && !predictedCanAttackAdjacent && threatDist <= playerThreatRadiusTiles)
                    {
                        Vector3 bestSafeTarget = chosenTarget;
                        float bestSafeDistToGoal = float.MaxValue;
                        bool foundSafe = false;

                        for (int ox = -1; ox <= 1; ox++)
                        {
                            for (int oy = -1; oy <= 1; oy++)
                            {
                                if (ox == 0 && oy == 0)
                                    continue;

                                Vector3 altTarget = from + new Vector3(ox * stepSize, oy * stepSize, 0f);

                                // Stay on the board.
                                if (gridManager != null && !gridManager.TryGetTileAtWorldPosition(altTarget, out _))
                                    continue;

                                int altThreat = GetThreatDistanceTiles(altTarget);
                                if (altThreat <= playerThreatRadiusTiles)
                                    continue;

                                float distToGoal = (chosenTarget - altTarget).sqrMagnitude;
                                if (distToGoal < bestSafeDistToGoal)
                                {
                                    bestSafeDistToGoal = distToGoal;
                                    bestSafeTarget = altTarget;
                                    foundSafe = true;
                                }
                            }
                        }

                        if (foundSafe)
                        {
                            chosenTarget = bestSafeTarget;
                        }
                        // else: no safe tile and not an immediate kill -> fall back to original move (risk it)
                    }
                }

                MoveAIUnitOneStep(unit, chosenTarget, stepSize);
            }
        }
    }

    void MoveAIUnitOneStep(Unit unit, Vector3 targetPosition, float tileSize)
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
            if (targetUnit.isPlayerOwned == unit.isPlayerOwned)
            {
                return;
            }

            // Enemy: attack
            unit.hasAttackedThisTurn = true;
            unit.RegisterMove();
            bool killed = unit.Attack(targetUnit);

            if (killed)
            {
                unit.transform.position = newPos;

                if (SoundManager.Instance != null)
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

            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayMove();
            }
        }

        // After moving, if the AI unit has not attacked yet,
        // look for an adjacent enemy to attack (move-then-attack).
        if (!unit.hasAttackedThisTurn)
        {
            float maxDist = 1.5f * tileSize;
            float minDist = 0.1f * tileSize;
            Unit[] allUnits = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            Unit bestEnemy = null;
            float bestDistSq = float.MaxValue;

            foreach (Unit other in allUnits)
            {
                if (other == null || other.isPlayerOwned == unit.isPlayerOwned)
                    continue;

                float dSq = (other.transform.position - unit.transform.position).sqrMagnitude;
                if (dSq < bestDistSq && dSq >= minDist * minDist && dSq <= maxDist * maxDist)
                {
                    bestDistSq = dSq;
                    bestEnemy = other;
                }
            }

            if (bestEnemy != null)
            {
                unit.hasAttackedThisTurn = true;
                bool killed = unit.Attack(bestEnemy);

                if (killed)
                {
                    unit.transform.position = bestEnemy.transform.position;
                }
            }
        }

        // Check for city capture after moving or killing
        City city = GridUtils.GetCityAtPosition(unit.transform.position);
        if (city != null && city.isPlayerOwned && !unit.isPlayerOwned)
        {
            OnCityCaptured(false, city);
        }
    }

    public void OnCityCaptured(bool capturedByPlayer, City capturedCity = null)
    {
        if (gameOver)
            return;

        if (capturedCity != null && capturedCity.isPlayerOwned != capturedByPlayer)
        {
            capturedCity.isPlayerOwned = capturedByPlayer;
            OwnedSprite cityOwnerVisual = capturedCity.GetComponent<OwnedSprite>();
            if (cityOwnerVisual != null)
            {
                cityOwnerVisual.SetOwner(capturedByPlayer);
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

        int winnerSeatIndex = capturedByPlayer ? 0 : 1;
        string message;
        if (currentMode == GameMode.PlayByPost && TryComputePbpLocalResult(winnerSeatIndex, out bool didLocalWin, out _))
        {
            message = didLocalWin ? "You won!" : "You lost!";
            ConfigurePbpEndgameFromCapture(didLocalWin);
        }
        else
        {
            message = capturedByPlayer ? "You Win!" : "You Lose!";
        }

        if (SoundManager.Instance != null)
        {
            // In Play-by-Post both sides are human-controlled, so always treat game-over as a "win" cue.
            bool playWinCue = (currentMode == GameMode.VsAI) ? capturedByPlayer : true;
            SoundManager.Instance.PlayGameOver(playWinCue);
        }

        ShowGameOverPopup(message);
    }

    void CollectPlayerIncome()
    {
        if (gameOver) return;

        int income = 0;
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
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

        int income = 0;
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            if (!city.isPlayerOwned)
            {
                income += goldPerCity;
            }
        }

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
    HashSet<TileVisibility> ComputeVisibilityForSide(bool sideIsPlayerOwned)
    {
        HashSet<TileVisibility> visibleTiles = new HashSet<TileVisibility>();

        if (gridManager == null)
            return visibleTiles;

        // Reveal around cities owned by this side
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            if (city.isPlayerOwned != sideIsPlayerOwned)
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
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            if (unit.isPlayerOwned != sideIsPlayerOwned)
                continue;

            if (!gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility originTile))
                continue;

            for (int dx = -visibilityRadius; dx <= visibilityRadius; dx++)
            {
                for (int dy = -visibilityRadius; dy <= visibilityRadius; dy++)
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

    public void RecalculatePlayerVisibility()
    {
        if (gridManager == null)
            return;

        bool currentSideIsPlayerOwned = GetViewerIsPlayerOwned();

        // Reset current visibility for this side (keep per-side explored memory)
        foreach (TileVisibility tile in gridManager.GetAllTiles())
        {
            tile.SetVisibleForSide(false, currentSideIsPlayerOwned);
        }

        // Compute which tiles should be visible for this side
        HashSet<TileVisibility> visibleTiles = ComputeVisibilityForSide(currentSideIsPlayerOwned);
        foreach (TileVisibility tile in visibleTiles)
        {
            tile.SetVisibleForSide(true, currentSideIsPlayerOwned);
        }

        // Hide enemy units that are not in visible tiles
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            bool isCurrentSideUnit = unit.isPlayerOwned == currentSideIsPlayerOwned;
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
        return Path.Combine(Application.persistentDataPath, autoSaveFileName);
    }

    private string GetPrimaryAutosavePathForCurrentMode()
    {
        // Primary autosave only: keep SP and PBp isolated from each other.
        if (currentMode == GameMode.VsAI)
        {
            return Path.Combine(Application.persistentDataPath, SinglePlayerPrimarySaveFileName);
        }

        if (currentMode == GameMode.PlayByPost)
        {
            return Path.Combine(Application.persistentDataPath, PlayByPostPrimarySaveFileName);
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
        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        string safeGameId = SanitizeGameIdForFileName(gameId);
        string directory = Path.Combine(Application.persistentDataPath, PlayByPostPerGameSaveFolderName);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{PlayByPostPerGameSavePrefix}{safeGameId}.json");
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

    private void SavePlayByPostPerGameSnapshot()
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

        WritePlayByPostSnapshotFile(snapshotPath);
    }

    private void SavePlayByPostPerGameSnapshot(string snapshotJson, int snapshotRoundTurn, bool snapshotIsPlayerTurn, string snapshotGameId)
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
            snapshotIsPlayerTurn);
    }

    private void WritePlayByPostSnapshotFile(string path)
    {
        WritePlayByPostSnapshotFile(path, null, currentGameId, turnNumber, isPlayerTurn);
    }

    private void WritePlayByPostSnapshotFile(
        string path,
        string snapshotJson,
        string gameIdForLog,
        int snapshotRoundTurn,
        bool snapshotIsPlayerTurn)
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
                lastKnownIsPlayerTurn: isPlayerTurn);
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
            mode = currentMode.ToString(),
            aiDifficulty = aiDifficulty.ToString(),
            isPlayerTurn = isPlayerTurn,
            turnNumber = turnNumber,
            playerGold = playerGold,
            aiGold = aiGold,
            gameOver = gameOver,
            visibilityRadius = visibilityRadius
        };

        ApplyTypedDisplayNameMetadata(save);

        // Cities
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            save.cities.Add(new SavedCity
            {
                x = city.x,
                y = city.y,
                isPlayerOwned = city.isPlayerOwned,
                hasRecruitedThisTurn = city.hasRecruitedThisTurn
            });
        }

        // Units
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            Vector3 pos = unit.transform.position;
            save.units.Add(new SavedUnit
            {
                unitTypeId = unit.UnitTypeId,
                isPlayerOwned = unit.isPlayerOwned,
                x = pos.x,
                y = pos.y,
                z = pos.z,
                currentHealth = unit.currentHealth,
                movesUsedThisTurn = unit.movesUsedThisTurn,
                hasAttackedThisTurn = unit.hasAttackedThisTurn
            });
        }

        // Tiles (seen state per side)
        foreach (TileVisibility tile in gridManager.GetAllTiles())
        {
            tile.GetSeenState(out bool playerSeen, out bool opponentSeen);
            save.tiles.Add(new SavedTile
            {
                x = tile.gridX,
                y = tile.gridY,
                playerSeen = playerSeen,
                opponentSeen = opponentSeen
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
            lastKnownIsPlayerTurn: saveForExport.isPlayerTurn);
        return true;
    }

    private static int ComputeTransportSeq(GameSave s)
    {
        return ComputeTransportSeq(s.turnNumber, s.isPlayerTurn);
    }

    private static int ComputeTransportSeq(int roundTurn, bool turnIsPlayer)
    {
        int clampedRoundTurn = System.Math.Max(0, roundTurn);
        return clampedRoundTurn * 2 + (turnIsPlayer ? 0 : 1);
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
            if (loadedModeIsPbp)
            {
                int loadedProtocolVersion = save.protocolVersion; // missing in older JSON => 0
                if (loadedProtocolVersion <= 0)
                {
                    string loadedGameId = string.IsNullOrWhiteSpace(save.gameId) ? "<none>" : save.gameId;
                    Debug.LogError(
                        $"PBp load blocked: protocolVersion is missing or invalid ({loadedProtocolVersion}), supported={SupportedPbpProtocolVersion} (gameId={loadedGameId}).");
                    return false;
                }

                if (loadedProtocolVersion != SupportedPbpProtocolVersion)
                {
                    string loadedGameId = string.IsNullOrWhiteSpace(save.gameId) ? "<none>" : save.gameId;
                    Debug.LogError(
                        $"PBp load blocked: protocolVersion={loadedProtocolVersion} does not match supported={SupportedPbpProtocolVersion} (gameId={loadedGameId}).");
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

            // AI difficulty (optional for older saves)
            aiDifficulty = AIDifficulty.Level1;
            if (!string.IsNullOrEmpty(save.aiDifficulty) &&
                System.Enum.TryParse(save.aiDifficulty, out AIDifficulty loadedDifficulty))
            {
                aiDifficulty = loadedDifficulty;
            }
            SetCurrentGameId(string.IsNullOrEmpty(save.gameId) ? System.Guid.NewGuid().ToString() : save.gameId);
            if (currentMode == GameMode.PlayByPost)
            {
                PersistCurrentPbpGameIdIfNeeded();
            }
            UpdateKnownTypedDisplayNames(save);
            isPlayerTurn = save.isPlayerTurn;
            turnNumber = save.turnNumber;
            playerGold = save.playerGold;
            aiGold = save.aiGold;
            gameOver = save.gameOver;
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
            bool viewerIsPlayerOwnedForLoad = GetViewerIsPlayerOwned();
            int unitCountBeforeClear = 0;
            int unitCountAfterSpawn = 0;
            int duplicateOwnerTileSlots = 0;
            string snapshotWriteMode = "none";
            int pbpSeat = 0;
            bool pbpHasSeat = false;
            string pbpSeatTextForLog = "<none>";

            // Clear units
            Unit[] existingUnits = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
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
            City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
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
                        city.isPlayerOwned = c.isPlayerOwned;
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
                    u.isPlayerOwned,
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
                    unit.currentHealth = Mathf.Clamp(u.currentHealth, 1, unit.maxHealth);
                    unit.movesUsedThisTurn = Mathf.Clamp(u.movesUsedThisTurn, 0, unit.maxMovesPerTurn);
                    unit.hasAttackedThisTurn = u.hasAttackedThisTurn;
                    bool isCurrentSideUnit = true;
                    if (currentMode == GameMode.PlayByPost)
                    {
                        isCurrentSideUnit = unit.isPlayerOwned == viewerIsPlayerOwnedForLoad;
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

            Unit[] unitsAfterSpawn = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
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
                        tile.SetSeenState(t.playerSeen, t.opponentSeen, true);
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
                bool localIsPlayerOne = pbpSeat == 0;
                bool currentSideIsPlayerOne = isPlayerTurn;
                bool localTurn = pbpHasSeat && (localIsPlayerOne == currentSideIsPlayerOne);
                pbpSeatTextForLog = pbpHasSeat ? pbpSeat.ToString() : "<none>";
                isPlayByPostWaitingForExport = !localTurn;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
                {
                    Debug.Log(
                        $"[PBpLoadSeat] loadedGameId={save.gameId} seat={pbpSeatTextForLog} hasSeat={pbpHasSeat} viewerIsPlayerOwned={viewerIsPlayerOwnedForLoad} loadedIsPlayerTurn={isPlayerTurn} isWaitingForExport={isPlayByPostWaitingForExport} canAdvance={CanAdvanceTurn()}");
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
            if (currentMode == GameMode.PlayByPost)
            {
                if (!string.IsNullOrWhiteSpace(rawLoadedJson))
                {
                    SavePlayByPostPerGameSnapshot(
                        snapshotJson: rawLoadedJson,
                        snapshotRoundTurn: save.turnNumber,
                        snapshotIsPlayerTurn: save.isPlayerTurn,
                        snapshotGameId: save.gameId);
                    snapshotWriteMode = "rawJson";
                }
                else
                {
                    SavePlayByPostPerGameSnapshot();
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

            SaveManifestService.RecordLoadApplied(
                currentGameId,
                currentMode,
                gameOver,
                lastKnownRoundTurn: turnNumber,
                lastKnownIsPlayerTurn: isPlayerTurn);
            GameplayInputOrchestrator.ResetTransientInputState();

            if (currentMode == GameMode.VsAI && !isPlayerTurn && !gameOver)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[Turn] Resuming AI turn after load.");
#endif
                StartCoroutine(AITurn());
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

    public bool TrySpendGold(bool forPlayer, int amount)
    {
        if (amount <= 0)
            return true;

        bool shouldPlayInvalid = false;
        if (SoundManager.Instance != null && !gameOver)
        {
            if (currentMode == GameMode.VsAI)
            {
                // Only play invalid for the human player's gold in Vs AI.
                shouldPlayInvalid = forPlayer && isPlayerTurn;
            }
            else if (currentMode == GameMode.PlayByPost)
            {
                // In Play-by-Post, both banks can be human-controlled depending on whose turn it is.
                bool spendingSideIsActive = isPlayerTurn == forPlayer;
                shouldPlayInvalid = spendingSideIsActive && IsHumanTurn();
            }
            else
            {
                // Fallback: treat "forPlayer" as human.
                shouldPlayInvalid = forPlayer;
            }
        }

        if (forPlayer)
        {
            if (playerGold < amount)
            {
                if (shouldPlayInvalid)
                {
                    SoundManager.Instance.PlayInvalid();
                }
                return false;
            }

            playerGold -= amount;
            return true;
        }
        else
        {
            if (aiGold < amount)
            {
                if (shouldPlayInvalid)
                {
                    SoundManager.Instance.PlayInvalid();
                }
                return false;
            }

            aiGold -= amount;
            return true;
        }
    }

    public void AddGold(bool forPlayer, int amount)
    {
        if (amount <= 0)
            return;

        if (forPlayer)
        {
            playerGold += amount;
        }
        else
        {
            aiGold += amount;
        }
    }

    /// <summary>
    /// Mutates the given save so that it represents the start
    /// of the next side's turn for Play-by-Post exports.
    /// </summary>
    private void PreparePlayByPostNextTurnSnapshot(GameSave save)
    {
        // Determine which side will act next.
        bool nextIsPlayer = !isPlayerTurn; // Player 1 -> Player 2, Player 2 -> Player 1
        save.isPlayerTurn = nextIsPlayer;

        // Advance the turn counter only when we wrap back to Player 1.
        save.turnNumber = turnNumber;
        if (!isPlayerTurn && nextIsPlayer)
        {
            // We just finished Player 2 locally; next save is Player 1, new round.
            save.turnNumber = turnNumber + 1;
        }

        // Reset recruitment flags for the side whose turn is starting
        // and compute their income from owned cities.
        int income = 0;
        foreach (SavedCity city in save.cities)
        {
            if (city.isPlayerOwned == nextIsPlayer)
            {
                city.hasRecruitedThisTurn = false;
                income += goldPerCity;
            }
        }

        if (income > 0)
        {
            if (nextIsPlayer)
            {
                save.playerGold += income;
            }
            else
            {
                save.aiGold += income;
            }
        }

        // Reset movement for units belonging to the side
        // that will act next so they have fresh moves.
        foreach (SavedUnit unit in save.units)
        {
            if (unit.isPlayerOwned == nextIsPlayer)
            {
                unit.movesUsedThisTurn = 0;
                unit.hasAttackedThisTurn = false;
            }
        }
    }
}
