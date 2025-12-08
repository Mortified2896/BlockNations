using UnityEngine;

public class Unit : MonoBehaviour
{
    [Header("Owner")]
    public bool isPlayerOwned = true;

    [Header("Turn State")]
    public int maxMovesPerTurn = 1;
    [HideInInspector] public int movesUsedThisTurn = 0;
    public bool hasMovedThisTurn => movesUsedThisTurn >= maxMovesPerTurn;

    public bool CanMoveThisTurn()
    {
        return movesUsedThisTurn < maxMovesPerTurn;
    }

    public void ResetMovementForTurn()
    {
        movesUsedThisTurn = 0;
    }

    public void RegisterMove()
    {
        if (movesUsedThisTurn < maxMovesPerTurn)
        {
            movesUsedThisTurn++;
        }
    }

    [Header("Visuals")]
    public SpriteRenderer moveOutline;

    public void UpdateMoveOutline(bool isPlayerTurn)
    {
        if (moveOutline == null) return;

        bool shouldShow = isPlayerTurn && isPlayerOwned && CanMoveThisTurn();
        moveOutline.enabled = shouldShow;
    }

    [Header("City Link")]
    public City currentCity;

    [Header("Stats")]
    public int maxHealth = 2;
    public int currentHealth = 2;
    public int attack = 1;
    public int defense = 0;

    private SpriteRenderer spriteRenderer;

    void Awake()
    {
        currentHealth = maxHealth;
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }
    }

    public bool Attack(Unit target)
    {
        if (target == null) return false;

        int rawDamage = attack;
        int mitigatedDamage = Mathf.Max(0, rawDamage - target.defense);

        if (mitigatedDamage <= 0)
        {
            Debug.Log(name + " attacked " + target.name + " but did no damage.");
            return false;
        }

        target.currentHealth -= mitigatedDamage;
        Debug.Log(name + " attacked " + target.name + " for " + mitigatedDamage +
                  " damage. Target HP: " + target.currentHealth + "/" + target.maxHealth);

        if (target.currentHealth <= 0)
        {
            target.Die();
            return true;
        }

        return false;
    }

    public void Die()
    {
        // If linked to a city, clear that reference
        if (currentCity != null && currentCity.stationedUnit != null)
        {
            if (currentCity.stationedUnit == gameObject)
            {
                currentCity.stationedUnit = null;
            }
        }

        Debug.Log(name + " has died.");
        Object.Destroy(gameObject);
    }

    public void SetFogVisibility(bool isVisible)
    {
        if (spriteRenderer != null && !isPlayerOwned)
        {
            spriteRenderer.enabled = isVisible;
        }
    }
}
