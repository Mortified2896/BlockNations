using UnityEngine;
using UnityEngine.Serialization;

public class Unit : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string unitTypeId = UnitRegistry.WarriorTypeId;

    [Header("Owner")]
    public bool isPlayerOwned = true;

    [Header("Turn State")]
    public int maxMovesPerTurn = 1;
    [HideInInspector] public int movesUsedThisTurn = 0;
    public bool hasMovedThisTurn => UsesCommittedMoveActionThisTurn() ? movesUsedThisTurn > 0 : movesUsedThisTurn >= maxMovesPerTurn;
    public int maxAttacksPerTurn = 1;
    [HideInInspector] public int attacksUsedThisTurn = 0;

    public bool CanMoveThisTurn()
    {
        return GetRemainingMoveRangeThisTurn() > 0;
    }

    public int GetRemainingMoveRangeThisTurn()
    {
        if (UsesCommittedMoveActionThisTurn())
        {
            return movesUsedThisTurn > 0 ? 0 : maxMovesPerTurn;
        }

        return Mathf.Max(0, maxMovesPerTurn - movesUsedThisTurn);
    }

    public bool CanAttackThisTurn()
    {
        if (attacksUsedThisTurn >= maxAttacksPerTurn)
        {
            return false;
        }

        if (!CanAttackAfterMoving && movesUsedThisTurn > 0)
        {
            return false;
        }

        return true;
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
    [SerializeField] private SpriteRenderer presentationRenderer;

    public void UpdateMoveOutline(bool isTurnForThisUnit)
    {
        if (moveOutline == null) return;

        bool shouldShow = isTurnForThisUnit && CanMoveThisTurn();
        moveOutline.enabled = shouldShow;
    }

    [Header("City Link")]
    public City currentCity;

    [Header("Stats")]
    [FormerlySerializedAs("maxHealth")]
    public int maxHealthUnits = CombatValues.FromDisplay(1);
    public int currentHealthUnits = CombatValues.FromDisplay(1);
    [FormerlySerializedAs("attack")]
    public int attackUnits = CombatValues.FromDisplay(1);
    public int attackRange = 1;
    public bool canAttackAfterMoving = true;
    [FormerlySerializedAs("defense")]
    public int defenseUnits = CombatValues.FromDisplay(0);

    private UnitDefinition resolvedDefinition;
    private UnitHealthLabel healthLabel;

    public string UnitTypeId => UnitRegistry.NormalizeTypeId(unitTypeId);
    public string DisplayName => resolvedDefinition != null ? resolvedDefinition.DisplayName : UnitRegistry.GetDefinitionOrDefault(UnitTypeId).DisplayName;
    public int VisionRange => resolvedDefinition != null ? resolvedDefinition.VisionRange : UnitRegistry.GetDefinitionOrDefault(UnitTypeId).VisionRange;
    public int AttackRange => resolvedDefinition != null ? resolvedDefinition.AttackRange : UnitRegistry.GetDefinitionOrDefault(UnitTypeId).AttackRange;
    public bool CanAttackAfterMoving => resolvedDefinition != null ? resolvedDefinition.CanAttackAfterMoving : UnitRegistry.GetDefinitionOrDefault(UnitTypeId).CanAttackAfterMoving;
    public bool AdvancesIntoDefenderTileOnKill => AttackRange <= 1;
    public SpriteRenderer PrimarySpriteRenderer => presentationRenderer;
    public bool IsPresentationVisible => presentationRenderer == null || presentationRenderer.enabled;

    void Awake()
    {
        if (presentationRenderer == null)
        {
            presentationRenderer = ResolvePresentationRenderer();
        }

        healthLabel = GetComponent<UnitHealthLabel>();
        if (healthLabel == null)
        {
            healthLabel = gameObject.AddComponent<UnitHealthLabel>();
        }
        ApplyDefinition(UnitTypeId, preserveCurrentHealth: currentHealthUnits > 0);
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
        maxHealthUnits = definition.MaxHealthUnits;
        attackUnits = definition.AttackUnits;
        attackRange = definition.AttackRange;
        canAttackAfterMoving = definition.CanAttackAfterMoving;
        defenseUnits = definition.DefenseUnits;

        if (!preserveCurrentHealth || currentHealthUnits <= 0)
        {
            currentHealthUnits = maxHealthUnits;
        }
        else
        {
            currentHealthUnits = Mathf.Clamp(currentHealthUnits, 1, maxHealthUnits);
        }

        movesUsedThisTurn = Mathf.Clamp(movesUsedThisTurn, 0, maxMovesPerTurn);
        attacksUsedThisTurn = Mathf.Clamp(attacksUsedThisTurn, 0, maxAttacksPerTurn);
        RefreshHealthPresentation();
        return true;
    }

    public void SetCurrentHealthUnits(int value)
    {
        currentHealthUnits = Mathf.Clamp(value, 0, maxHealthUnits);
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

        int rawDamageUnits = attackUnits;
        int mitigatedDamageUnits = Mathf.Max(0, rawDamageUnits - target.defenseUnits);

        if (mitigatedDamageUnits <= 0)
        {
            Debug.Log(name + " attacked " + target.name + " but did no damage.");
            return false;
        }

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayAttack();
        }

        target.SetCurrentHealthUnits(target.currentHealthUnits - mitigatedDamageUnits);
        Debug.Log(
            name + " attacked " + target.name + " for " + CombatValues.FormatUnits(mitigatedDamageUnits) +
            " damage. Target HP: " + CombatValues.FormatRatio(target.currentHealthUnits, target.maxHealthUnits));

        if (target.currentHealthUnits <= 0)
        {
            target.Die();
            return true;
        }

        return false;
    }

    public bool IsTargetInAttackRange(int tileDistance)
    {
        return tileDistance > 0 && tileDistance <= AttackRange;
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
        if (presentationRenderer == null)
            return;

        if (isCurrentSideUnit)
        {
            presentationRenderer.enabled = true;
            RefreshHealthPresentation();
            return;
        }

        presentationRenderer.enabled = isVisible;
        RefreshHealthPresentation();
    }

    private SpriteRenderer ResolvePresentationRenderer()
    {
        SpriteRenderer rootRenderer = GetComponent<SpriteRenderer>();
        if (rootRenderer != null)
        {
            return rootRenderer;
        }

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer == moveOutline)
            {
                continue;
            }

            return renderer;
        }

        return null;
    }

    private bool UsesCommittedMoveActionThisTurn()
    {
        return UnitTypeId == UnitRegistry.RiderTypeId;
    }
}
