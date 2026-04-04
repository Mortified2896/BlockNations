using UnityEngine;

public class Unit : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string unitTypeId = UnitRegistry.WarriorTypeId;

    [Header("Owner")]
    public bool isPlayerOwned = true;

    [Header("Turn State")]
    public int maxMovesPerTurn = 1;
    [HideInInspector] public int movesUsedThisTurn = 0;
    public bool hasMovedThisTurn => movesUsedThisTurn >= maxMovesPerTurn;
    public int maxAttacksPerTurn = 1;
    [HideInInspector] public int attacksUsedThisTurn = 0;

    public bool CanMoveThisTurn()
    {
        return movesUsedThisTurn < maxMovesPerTurn;
    }

    public bool CanAttackThisTurn()
    {
        return attacksUsedThisTurn < maxAttacksPerTurn;
    }

    public void ResetMovementForTurn()
    {
        movesUsedThisTurn = 0;
        attacksUsedThisTurn = 0;
    }

    public void RegisterMove()
    {
        if (movesUsedThisTurn < maxMovesPerTurn)
        {
            movesUsedThisTurn++;
        }
    }

    public void RegisterMove(int moveCount)
    {
        for (int i = 0; i < moveCount; i++)
        {
            RegisterMove();
        }
    }

    [Header("Visuals")]
    public SpriteRenderer moveOutline;

    public void UpdateMoveOutline(bool isTurnForThisUnit)
    {
        if (moveOutline == null) return;

        bool shouldShow = isTurnForThisUnit && CanMoveThisTurn();
        moveOutline.enabled = shouldShow;
    }

    [Header("City Link")]
    public City currentCity;

    [Header("Stats")]
    public int maxHealth = 1;
    public int currentHealth = 1;
    public int attack = 1;
    public int defense = 0;

    private SpriteRenderer spriteRenderer;
    private UnitDefinition resolvedDefinition;
    private UnitHealthLabel healthLabel;

    public string UnitTypeId => UnitRegistry.NormalizeTypeId(unitTypeId);
    public string DisplayName => resolvedDefinition != null ? resolvedDefinition.DisplayName : UnitRegistry.GetDefinitionOrDefault(UnitTypeId).DisplayName;
    public int VisionRange => resolvedDefinition != null ? resolvedDefinition.VisionRange : UnitRegistry.GetDefinitionOrDefault(UnitTypeId).VisionRange;
    public SpriteRenderer PrimarySpriteRenderer => spriteRenderer;
    public bool IsPresentationVisible => spriteRenderer == null || spriteRenderer.enabled;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        healthLabel = GetComponent<UnitHealthLabel>();
        if (healthLabel == null)
        {
            healthLabel = gameObject.AddComponent<UnitHealthLabel>();
        }
        ApplyDefinition(UnitTypeId, preserveCurrentHealth: currentHealth > 0);
        RefreshHealthPresentation();
    }

    public bool ApplyDefinition(string requestedUnitTypeId, bool preserveCurrentHealth)
    {
        if (!UnitRegistry.TryGetDefinition(requestedUnitTypeId, out UnitDefinition definition))
        {
            return false;
        }

        resolvedDefinition = definition;
        unitTypeId = definition.TypeId;
        maxMovesPerTurn = definition.MaxMovesPerTurn;
        maxAttacksPerTurn = definition.MaxAttacksPerTurn;
        maxHealth = definition.MaxHealth;
        attack = definition.Attack;
        defense = definition.Defense;

        if (!preserveCurrentHealth || currentHealth <= 0)
        {
            currentHealth = maxHealth;
        }
        else
        {
            currentHealth = Mathf.Clamp(currentHealth, 1, maxHealth);
        }

        movesUsedThisTurn = Mathf.Clamp(movesUsedThisTurn, 0, maxMovesPerTurn);
        attacksUsedThisTurn = Mathf.Clamp(attacksUsedThisTurn, 0, maxAttacksPerTurn);
        RefreshHealthPresentation();
        return true;
    }

    public void SetCurrentHealth(int value)
    {
        currentHealth = Mathf.Clamp(value, 0, maxHealth);
        RefreshHealthPresentation();
    }

    public void RefreshHealthPresentation()
    {
        if (healthLabel == null)
        {
            healthLabel = GetComponent<UnitHealthLabel>();
            if (healthLabel == null)
            {
                healthLabel = gameObject.AddComponent<UnitHealthLabel>();
            }
        }

        if (healthLabel != null)
        {
            healthLabel.Refresh();
        }
    }

    public void RegisterAttack()
    {
        if (attacksUsedThisTurn < maxAttacksPerTurn)
        {
            attacksUsedThisTurn++;
        }
    }

    public void ConsumeRemainingAttacksForTurn()
    {
        attacksUsedThisTurn = maxAttacksPerTurn;
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

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayAttack();
        }

        target.SetCurrentHealth(target.currentHealth - mitigatedDamage);
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

        if (healthLabel == null)
        {
            healthLabel = GetComponent<UnitHealthLabel>();
        }

        if (healthLabel != null)
        {
            healthLabel.Hide();
        }

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayUnitDown();
        }

        // Destroy() happens end-of-frame, and visibility updates can re-enable renderers.
        // Deactivate immediately so the dead unit can't briefly overlap/tint the attacker.
        gameObject.SetActive(false);

        Object.Destroy(gameObject);
    }

    /// <summary>
    /// Controls whether the unit sprite is visible under fog. Current side units stay visible;
    /// opposing units are only visible when their tile is visible to the active side.
    /// </summary>
    public void SetFogVisibility(bool isVisible, bool isCurrentSideUnit)
    {
        if (spriteRenderer == null)
            return;

        if (isCurrentSideUnit)
        {
            spriteRenderer.enabled = true;
            RefreshHealthPresentation();
            return;
        }

        spriteRenderer.enabled = isVisible;
        RefreshHealthPresentation();
    }
}
