using UnityEngine;
using UnityEngine.Serialization;

public class Unit : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string unitTypeId = UnitRegistry.WarriorTypeId;

    [Header("Owner")]
    public int ownerSeatIndex = -1;
    public bool isPlayerOwned = true;

    [Header("Turn State")]
    public int maxMovesPerTurn = 1;
    [HideInInspector] public int movesUsedThisTurn = 0;
    public bool hasMovedThisTurn => UsesCommittedMoveActionThisTurn() ? movesUsedThisTurn > 0 : movesUsedThisTurn >= maxMovesPerTurn;
    public int maxAttacksPerTurn = 1;
    [HideInInspector] public int attacksUsedThisTurn = 0;
    // Presentation status, separate from the action budget and gameplay save format.
    public int SurprisedRound { get; private set; } = -1;
    public bool IsSurprised { get; private set; }
    public void SetSurprisedRound(int round, int currentRound)
    {
        SurprisedRound = round;
        IsSurprised = round >= 1 && round == currentRound;
        UnitSurpriseLabel label = GetComponent<UnitSurpriseLabel>();
        if (label == null && round >= 1) label = gameObject.AddComponent<UnitSurpriseLabel>();
        if (label != null) label.Refresh(this);
    }

    public bool CanMoveThisTurn()
    {
        return GetRemainingMoveRangeThisTurn() > 0;
    }

    public int GetRemainingMoveRangeThisTurn()
    {
        return BlockNations.Simulation.SimulationRules.RemainingMoves(UsesCommittedMoveActionThisTurn(), maxMovesPerTurn,
            movesUsedThisTurn, AttackEndsMovement, attacksUsedThisTurn);
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
        bool hasAttackTargetNow = isTurnForThisUnit && !CanMoveThisTurn() && CanAttackThisTurn() &&
                                  UnitSelectionManager.Instance != null &&
                                  UnitSelectionManager.Instance.HasLegalAttackTargetNow(this);
        moveOutline.enabled = TryGetActionOutlineStyle(isTurnForThisUnit, hasAttackTargetNow,
            out Color color, out float scale);
        if (!moveOutline.enabled) return;
        moveOutline.color = color;
        ApplyMoveOutlineScale(scale);
    }

    public struct ActionOutlinePresentation
    {
        public Sprite sprite;
        public Rect bounds;
        public Color color;
    }

    // Record action readiness, not the renderer's possibly stale enabled/tint state.
    // The caller supplies explicit turn ownership and fair legal-target information.
    public bool TryGetActionOutlinePresentation(bool isTurnForThisUnit, bool hasAttackTargetNow,
        out ActionOutlinePresentation presentation)
    {
        presentation = default;
        if (moveOutline == null || moveOutline.sprite == null || currentHealthUnits <= 0 ||
            !TryGetActionOutlineStyle(isTurnForThisUnit, hasAttackTargetNow, out Color color, out float scale)) return false;
        Transform outline = moveOutline.transform;
        Vector3 baseScale = hasMoveOutlineBaseLocalScale ? moveOutlineBaseLocalScale : outline.localScale;
        Matrix4x4 matrix = (outline.parent != null ? outline.parent.localToWorldMatrix : Matrix4x4.identity) *
            Matrix4x4.TRS(outline.localPosition, outline.localRotation, baseScale * Mathf.Max(0, scale));
        Bounds local = moveOutline.localBounds;
        Vector3 lower = matrix.MultiplyPoint3x4(local.min), upper = lower;
        for (int corner = 1; corner < 8; corner++)
        {
            Vector3 point = matrix.MultiplyPoint3x4(new Vector3(
                (corner & 1) == 0 ? local.min.x : local.max.x,
                (corner & 2) == 0 ? local.min.y : local.max.y,
                (corner & 4) == 0 ? local.min.z : local.max.z));
            lower = Vector3.Min(lower, point); upper = Vector3.Max(upper, point);
        }
        presentation = new ActionOutlinePresentation { sprite = moveOutline.sprite, color = color,
            bounds = Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y) };
        return true;
    }

    private bool TryGetActionOutlineStyle(bool isTurnForThisUnit, bool hasAttackTargetNow,
        out Color color, out float scale)
    {
        color = default; scale = 0;
        if (!isTurnForThisUnit) return false;
        if (CanMoveThisTurn()) { color = moveReadyOutlineColor; scale = moveReadyOutlineScaleMultiplier; return true; }
        if (!CanAttackThisTurn() || !hasAttackTargetNow) return false;
        color = attackReadyOutlineColor; scale = attackReadyOutlineScaleMultiplier;
        return true;
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
    public bool AttackEndsMovement => resolvedDefinition != null ? resolvedDefinition.AttackEndsMovement : UnitRegistry.GetDefinitionOrDefault(UnitTypeId).AttackEndsMovement;
    public bool AdvancesIntoDefenderTileOnKill => UnitActionRules.AdvancesIntoDefenderTileOnKill(AttackRange);
    public SpriteRenderer PrimarySpriteRenderer => presentationRenderer;
    public bool IsPresentationVisible => presentationRenderer == null || presentationRenderer.enabled;

    void Awake()
    {
        EnsureOwnerSeatInitialized();
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
        if (resolvedDefinition == null) ApplyDefinition(UnitTypeId, preserveCurrentHealth: currentHealthUnits > 0);
        else ApplyResolvedDefinition(resolvedDefinition, preserveCurrentHealth: currentHealthUnits > 0);
        RefreshHealthPresentation();
    }

    public void EnsureOwnerSeatInitialized()
    {
        if (ownerSeatIndex < 0)
        {
            ownerSeatIndex = isPlayerOwned ? 0 : 1;
        }

        SyncLegacyOwnershipBridge();
    }

    public void SetOwnerSeatIndex(int seatIndex)
    {
        ownerSeatIndex = Mathf.Max(0, seatIndex);
        SyncLegacyOwnershipBridge();
    }

    private void OnDestroy()
    {
        // Scene unloads, match resets and editor destruction can bypass Die().
        // Never leave a destroyed presentation object linked to a surviving city.
        if (currentCity != null && currentCity.stationedUnit == gameObject) currentCity.stationedUnit = null;
    }

    public void SyncLegacyOwnershipBridge()
    {
        isPlayerOwned = ownerSeatIndex == 0;
    }

    public bool ApplyDefinition(string requestedUnitTypeId, bool preserveCurrentHealth)
    {
        if (!UnitRegistry.TryGetDefinition(requestedUnitTypeId, out UnitDefinition definition))
        {
            return false;
        }

        return ApplyResolvedDefinition(definition, preserveCurrentHealth);
    }

    public bool ApplyResolvedDefinition(UnitDefinition definition, bool preserveCurrentHealth)
    {
        if (definition == null) return false;

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
        UnitSurpriseLabel surprise = GetComponent<UnitSurpriseLabel>();
        if (surprise == null) surprise = gameObject.AddComponent<UnitSurpriseLabel>();
        surprise.Refresh(this);
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
        movesUsedThisTurn = BlockNations.Simulation.SimulationRules.MovementUsedAfterAttack(AttackEndsMovement, maxMovesPerTurn, movesUsedThisTurn);
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
            return false;
        }

        if (SoundManager.Instance != null &&
            (TurnManager.Instance == null || !TurnManager.Instance.ShouldSuppressAIVsAIAudio()))
        {
            SoundManager.Instance.PlayAttack();
        }

        target.SetCurrentHealthUnits(target.currentHealthUnits - mitigatedDamageUnits);

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
