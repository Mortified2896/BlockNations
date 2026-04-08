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
        return UnitActionRules.GetRemainingMoveRangeThisTurn(UnitTypeId, maxMovesPerTurn, movesUsedThisTurn);
    }

    public bool CanAttackThisTurn()
    {
        return UnitActionRules.CanAttackThisTurn(
            CanAttackAfterMoving,
            maxAttacksPerTurn,
            attacksUsedThisTurn,
            movesUsedThisTurn);
    }

    public void ResetMovementForTurn()
    {
        movesUsedThisTurn = 0;
        attacksUsedThisTurn = 0;
    }

    public void RegisterMove()
    {
        movesUsedThisTurn = UnitActionRules.RegisterMove(movesUsedThisTurn, maxMovesPerTurn);
    }

    public void RegisterMove(int moveCount)
    {
        movesUsedThisTurn = UnitActionRules.RegisterMove(movesUsedThisTurn, maxMovesPerTurn, moveCount);
    }

    [Header("Visuals")]
    public SpriteRenderer moveOutline;
    [SerializeField] private SpriteRenderer presentationRenderer;
    [SerializeField] private Color moveReadyOutlineColor = new Color(0.86415094f, 0.7444677f, 0.11576363f, 1f);
    [SerializeField] private Color attackReadyOutlineColor = new Color(0.68f, 0.42f, 0.39f, 1f);
    [SerializeField] private float moveReadyOutlineScaleMultiplier = 1f;
    [SerializeField] private float attackReadyOutlineScaleMultiplier = 0.85f;

    private Vector3 moveOutlineBaseLocalScale = Vector3.one;
    private bool hasMoveOutlineBaseLocalScale;

    public void UpdateMoveOutline(bool isTurnForThisUnit)
    {
        if (moveOutline == null) return;

        CacheMoveOutlineBaseLocalScale();

        if (!isTurnForThisUnit)
        {
            moveOutline.enabled = false;
            return;
        }

        bool canMove = CanMoveThisTurn();
        bool canAttack = CanAttackThisTurn();

        if (canMove)
        {
            moveOutline.color = moveReadyOutlineColor;
            ApplyMoveOutlineScale(moveReadyOutlineScaleMultiplier);
            moveOutline.enabled = true;
            return;
        }

        bool hasAttackTargetNow = canAttack &&
                                  UnitSelectionManager.Instance != null &&
                                  UnitSelectionManager.Instance.HasLegalAttackTargetNow(this);
        if (hasAttackTargetNow)
        {
            moveOutline.color = attackReadyOutlineColor;
            ApplyMoveOutlineScale(attackReadyOutlineScaleMultiplier);
            moveOutline.enabled = true;
            return;
        }

        moveOutline.enabled = false;
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
    public bool AdvancesIntoDefenderTileOnKill => UnitActionRules.AdvancesIntoDefenderTileOnKill(AttackRange);
    public SpriteRenderer PrimarySpriteRenderer => presentationRenderer;
    public bool IsPresentationVisible => presentationRenderer == null || presentationRenderer.enabled;

    void Awake()
    {
        CacheMoveOutlineBaseLocalScale();

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
        attacksUsedThisTurn = UnitActionRules.RegisterAttack(attacksUsedThisTurn, maxAttacksPerTurn);
    }

    public void ConsumeRemainingAttacksForTurn()
    {
        attacksUsedThisTurn = maxAttacksPerTurn;
    }

    public bool Attack(Unit target)
    {
        if (target == null) return false;

        int rawDamageUnits = attackUnits;
        int mitigatedDamageUnits = UnitActionRules.ComputeMitigatedDamage(rawDamageUnits, target.defenseUnits);

        if (mitigatedDamageUnits <= 0)
        {
            Debug.Log(name + " attacked " + target.name + " but did no damage.");
            return false;
        }

        if (SoundManager.Instance != null &&
            (TurnManager.Instance == null || !TurnManager.Instance.ShouldSuppressAIVsAIAudio()))
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
        return UnitActionRules.IsTargetInAttackRange(AttackRange, tileDistance);
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

        if (SoundManager.Instance != null &&
            (TurnManager.Instance == null || !TurnManager.Instance.ShouldSuppressAIVsAIAudio()))
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

    private void CacheMoveOutlineBaseLocalScale()
    {
        if (moveOutline == null || hasMoveOutlineBaseLocalScale)
        {
            return;
        }

        moveOutlineBaseLocalScale = moveOutline.transform.localScale;
        hasMoveOutlineBaseLocalScale = true;
    }

    private void ApplyMoveOutlineScale(float scaleMultiplier)
    {
        if (moveOutline == null)
        {
            return;
        }

        CacheMoveOutlineBaseLocalScale();
        float clampedMultiplier = Mathf.Max(0f, scaleMultiplier);
        moveOutline.transform.localScale = moveOutlineBaseLocalScale * clampedMultiplier;
    }

    private bool UsesCommittedMoveActionThisTurn()
    {
        return UnitActionRules.UsesCommittedMoveActionThisTurn(UnitTypeId);
    }
}
