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

        // Spawn at the city position for now
        Vector3 spawnPosition = transform.position;
        GameObject warrior = Instantiate(warriorPrefab, spawnPosition, Quaternion.identity);
        stationedUnit = warrior;
        hasRecruitedThisTurn = true;

        // Set up the Unit component with ownership and city link
        Unit unit = warrior.GetComponent<Unit>();
        if (unit != null)
        {
            unit.isPlayerOwned = isPlayerOwned;
            unit.currentCity = this;
        }

        // Apply ownership color if the unit has an OwnedSprite
        OwnedSprite owned = warrior.GetComponent<OwnedSprite>();
        if (owned != null)
        {
            owned.SetOwner(isPlayerOwned);
        }

        Debug.Log($"Spawned Warrior from city {name} at world position {spawnPosition}.");
    }
}
