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
            Debug.Log("City already has a unit stationed here, cannot recruit another right now.", this);
            return false;
        }

        if (hasRecruitedThisTurn)
        {
            Debug.Log("This city has already recruited a unit this turn.", this);
            return false;
        }

        return true;
    }

    public void SpawnWarrior()
    {
        if (warriorPrefab == null)
        {
            Debug.LogWarning("City has no Warrior prefab assigned.", this);
            return;
        }

        if (!CanRecruit())
        {
            return;
        }

        // Spawn at the city position for now, but only if no other unit is already there
        Vector3 spawnPosition = transform.position;
        if (GridUtils.IsTileOccupied(spawnPosition, null))
        {
            Debug.Log("Cannot spawn Warrior in city " + name + " because the tile is already occupied by a unit.", this);
            return;
        }

        // Pay the recruitment cost through the TurnManager
        if (TurnManager.Instance == null)
        {
            Debug.LogWarning("Cannot spawn Warrior because TurnManager instance is missing.", this);
            return;
        }

        if (!TurnManager.Instance.TrySpendGold(isPlayerOwned, TurnManager.Instance.warriorCost))
        {
            if (isPlayerOwned)
            {
                Debug.Log("Not enough gold to recruit a Warrior in " + name);
            }
            else
            {
                Debug.Log("AI lacks gold to recruit a Warrior in " + name);
            }
            return;
        }

        GameObject warrior = Instantiate(warriorPrefab, spawnPosition, Quaternion.identity);
        stationedUnit = warrior;
        hasRecruitedThisTurn = true;

        // Set up the Unit component with ownership and city link
        Unit unit = warrior.GetComponent<Unit>();
        if (unit != null)
        {
            unit.isPlayerOwned = isPlayerOwned;
            unit.currentCity = this;
            unit.ResetMovementForTurn();
            unit.UpdateMoveOutline(true);
        }

        // Apply ownership color if the unit has an OwnedSprite
        OwnedSprite owned = warrior.GetComponent<OwnedSprite>();
        if (owned != null)
        {
            owned.SetOwner(isPlayerOwned);
        }

        Debug.Log($"Spawned Warrior from city {name} at world position {spawnPosition}.");

        if (isPlayerOwned && TurnManager.Instance != null)
        {
            TurnManager.Instance.RecalculatePlayerVisibility();
        }
    }
}
