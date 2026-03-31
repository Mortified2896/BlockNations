using UnityEngine;

public class City : MonoBehaviour
{
    [Header("Owner")]
    public bool isPlayerOwned = false;

    [Header("Grid Position")]
    public int x;
    public int y;

    [Header("Units")]
    public GameObject warriorPrefab;
    public GameObject stationedUnit;          // unit currently in this city (if any)
    public bool hasRecruitedThisTurn = false; // to limit recruitment per turn

    public bool CanRecruit()
    {
        if (stationedUnit != null)
        {
            if (PbpDebugSettingsLoader.EnableInputLogs)
            {
                Debug.Log("City already has a unit stationed here, cannot recruit another right now.", this);
            }
            return false;
        }

        if (hasRecruitedThisTurn)
        {
            if (PbpDebugSettingsLoader.EnableInputLogs)
            {
                Debug.Log("This city has already recruited a unit this turn.", this);
            }
            return false;
        }

        return true;
    }

    private bool ShouldPlayInvalidForThisCity()
    {
        if (TurnManager.Instance == null)
            return false;

        if (!TurnManager.Instance.IsHumanTurn())
            return false;

        return TurnManager.Instance.IsCurrentSideOwner(isPlayerOwned);
    }

    private void PlayInvalidIfHuman()
    {
        if (SoundManager.Instance != null && ShouldPlayInvalidForThisCity())
        {
            SoundManager.Instance.PlayInvalid();
        }
    }

    public void SpawnWarrior()
    {
        TrySpawnUnit(UnitRegistry.WarriorTypeId);
    }

    public bool TrySpawnUnit(string unitTypeId)
    {
        string resolvedUnitTypeId = UnitRegistry.NormalizeTypeId(unitTypeId);
        if (!UnitRegistry.TryGetDefinition(resolvedUnitTypeId, out UnitDefinition definition))
        {
            Debug.LogWarning($"City cannot spawn unknown unit type '{unitTypeId}'.", this);
            PlayInvalidIfHuman();
            return false;
        }

        GameObject prefab = ResolveRecruitPrefab(resolvedUnitTypeId);
        if (prefab == null)
        {
            Debug.LogWarning($"City has no prefab assigned for {definition.DisplayName}.", this);
            PlayInvalidIfHuman();
            return false;
        }

        if (!CanRecruit())
        {
            PlayInvalidIfHuman();
            return false;
        }

        // Spawn at the city position for now, but only if no other unit is already there
        Vector3 spawnPosition = transform.position;
        if (GridUtils.IsTileOccupied(spawnPosition, null))
        {
            if (PbpDebugSettingsLoader.EnableInputLogs)
            {
                Debug.Log($"Cannot spawn {definition.DisplayName} in city {name} because the tile is already occupied by a unit.", this);
            }
            PlayInvalidIfHuman();
            return false;
        }

        // Pay the recruitment cost through the TurnManager
        if (TurnManager.Instance == null)
        {
            Debug.LogWarning($"Cannot spawn {definition.DisplayName} because TurnManager instance is missing.", this);
            PlayInvalidIfHuman();
            return false;
        }

        if (TurnManager.Instance.currentMode == TurnManager.GameMode.PlayByPost &&
            !TurnManager.Instance.CanLocalPlayerIssueCommands())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (PbpDebugSettingsLoader.EnableInputLogs)
            {
                Debug.Log($"Ignored SpawnWarrior in PBp while local commands are locked (city={name}).", this);
            }
#endif
            PlayInvalidIfHuman();
            return false;
        }

        if (!TurnManager.Instance.TrySpendGold(isPlayerOwned, definition.RecruitCost))
        {
            if (isPlayerOwned)
            {
                if (PbpDebugSettingsLoader.EnableInputLogs)
                {
                    Debug.Log($"Not enough gold to recruit a {definition.DisplayName} in {name}");
                }
            }
            else
            {
                if (PbpDebugSettingsLoader.EnableInputLogs)
                {
                    Debug.Log($"AI lacks gold to recruit a {definition.DisplayName} in {name}");
                }
            }

            // If the player attempted to recruit and still has no actions, auto-end can kick in.
            TurnManager.Instance.ScheduleAutoEndTurnCheck();
            return false;
        }

        GameObject spawnedUnit = TurnManager.Instance.InstantiateConfiguredUnit(
            resolvedUnitTypeId,
            prefab,
            spawnPosition,
            isPlayerOwned,
            this,
            resetTurnState: true);
        if (spawnedUnit == null)
        {
            PlayInvalidIfHuman();
            return false;
        }

        stationedUnit = spawnedUnit;
        hasRecruitedThisTurn = true;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayRecruit();
        }

        if (PbpDebugSettingsLoader.EnableInputLogs)
        {
            Debug.Log($"Spawned {definition.DisplayName} from city {name} at world position {spawnPosition}.");
        }

        if (isPlayerOwned && TurnManager.Instance != null)
        {
            TurnManager.Instance.RecalculatePlayerVisibility();
            TurnManager.Instance.AutoSaveIfEnabled();
            TurnManager.Instance.ScheduleAutoEndTurnCheck();
        }
        else if (!isPlayerOwned && TurnManager.Instance != null)
        {
            bool isCurrentSideUnit = TurnManager.Instance.currentMode != TurnManager.GameMode.PlayByPost || (TurnManager.Instance.isPlayerTurn == isPlayerOwned);
            Unit spawned = spawnedUnit.GetComponent<Unit>();
            if (spawned != null)
            {
                spawned.SetFogVisibility(true, isCurrentSideUnit);
            }
        }

        return true;
    }

    private GameObject ResolveRecruitPrefab(string unitTypeId)
    {
        if (unitTypeId == UnitRegistry.WarriorTypeId && warriorPrefab != null)
        {
            return warriorPrefab;
        }

        return TurnManager.Instance != null
            ? TurnManager.Instance.GetUnitPrefabForType(unitTypeId)
            : null;
    }
}
