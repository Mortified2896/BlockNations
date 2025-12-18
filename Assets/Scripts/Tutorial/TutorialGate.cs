using System;
using UnityEngine;

/// <summary>
/// Global gate used to restrict gameplay input during the tutorial.
/// TutorialOverlay sets these delegates per-step; gameplay scripts consult them before acting.
/// </summary>
public static class TutorialGate
{
    public static bool IsActive { get; private set; }

    public static Func<Unit, bool> CanSelectUnit;
    public static Func<Unit, Vector3, bool> CanMoveOrAttackToPosition;
    public static Func<City, bool> CanClickCity;
    public static Func<bool> CanRecruitWarrior;
    public static Func<bool> CanEndTurn;

    // Optional: during deterministic steps, highlight only one specific world target (tile or enemy).
    public static bool ForceSingleTargetHighlight;
    public static Vector3 ForcedTargetWorldPosition;
    public static bool ForcedTargetIsAttack;

    public static void SetActive(bool active)
    {
        IsActive = active;
        if (!active)
        {
            ClearAll();
        }
    }

    public static void ClearAll()
    {
        CanSelectUnit = null;
        CanMoveOrAttackToPosition = null;
        CanClickCity = null;
        CanRecruitWarrior = null;
        CanEndTurn = null;

        ForceSingleTargetHighlight = false;
        ForcedTargetWorldPosition = Vector3.zero;
        ForcedTargetIsAttack = false;
    }
}
