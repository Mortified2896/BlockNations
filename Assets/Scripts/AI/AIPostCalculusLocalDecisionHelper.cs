using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class AIPostCalculusLocalDecisionHelper
{
    public readonly struct ImmediateCityWinCandidate
    {
        public readonly Vector3 targetPosition;
        public readonly int targetX;
        public readonly int targetY;
        public readonly bool requiresAttack;

        public ImmediateCityWinCandidate(Vector3 targetPosition, int targetX, int targetY, bool requiresAttack)
        {
            this.targetPosition = targetPosition;
            this.targetX = targetX;
            this.targetY = targetY;
            this.requiresAttack = requiresAttack;
        }
    }

    public readonly struct AttackCandidate
    {
        public readonly Unit target;
        public readonly bool canKill;
        public readonly int predictedDamage;
        public readonly int baselineDistance;
        public readonly int baselineTargetHealth;
        public readonly int targetAttackUnits;
        public readonly bool targetOccupiesEnemyCity;

        public AttackCandidate(
            Unit target,
            bool canKill,
            int predictedDamage,
            int baselineDistance,
            int baselineTargetHealth,
            int targetAttackUnits,
            bool targetOccupiesEnemyCity)
        {
            this.target = target;
            this.canKill = canKill;
            this.predictedDamage = predictedDamage;
            this.baselineDistance = baselineDistance;
            this.baselineTargetHealth = baselineTargetHealth;
            this.targetAttackUnits = targetAttackUnits;
            this.targetOccupiesEnemyCity = targetOccupiesEnemyCity;
        }
    }

    public readonly struct MoveCandidate
    {
        public readonly Vector3 position;
        public readonly int targetX;
        public readonly int targetY;
        public readonly float baselineDistanceToGoal;
        public readonly bool immediatelyLosesKeyCity;

        public MoveCandidate(
            Vector3 position,
            int targetX,
            int targetY,
            float baselineDistanceToGoal,
            bool immediatelyLosesKeyCity)
        {
            this.position = position;
            this.targetX = targetX;
            this.targetY = targetY;
            this.baselineDistanceToGoal = baselineDistanceToGoal;
            this.immediatelyLosesKeyCity = immediatelyLosesKeyCity;
        }
    }

    public static bool HasFeature(AILocalDecisionFeatures enabledFeatures, AILocalDecisionFeatures feature)
    {
        return (enabledFeatures & feature) != 0;
    }

    public static string ToConfigValue(AILocalDecisionFeatures enabledFeatures)
    {
        if (enabledFeatures == AILocalDecisionFeatures.None)
        {
            return "none";
        }

        StringBuilder builder = new StringBuilder();
        AppendFeatureToken(builder, enabledFeatures, AILocalDecisionFeatures.OffensiveObviousWin, "offense");
        AppendFeatureToken(builder, enabledFeatures, AILocalDecisionFeatures.DefensiveVeto, "defense");
        AppendFeatureToken(builder, enabledFeatures, AILocalDecisionFeatures.ExchangeScoring, "exchange");
        return builder.Length > 0 ? builder.ToString() : "none";
    }

    public static bool TryChooseImmediateCityWin(
        AILocalDecisionFeatures enabledFeatures,
        IList<ImmediateCityWinCandidate> candidates,
        out ImmediateCityWinCandidate chosenCandidate)
    {
        chosenCandidate = default;
        if (!HasFeature(enabledFeatures, AILocalDecisionFeatures.OffensiveObviousWin) ||
            candidates == null ||
            candidates.Count == 0)
        {
            return false;
        }

        int bestIndex = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (bestIndex < 0 || IsBetterImmediateCityWinCandidate(candidates[i], candidates[bestIndex]))
            {
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            return false;
        }

        chosenCandidate = candidates[bestIndex];
        return true;
    }

    public static Unit ChooseAttackTarget(
        AILocalDecisionFeatures enabledFeatures,
        IList<AttackCandidate> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        int bestIndex = 0;
        for (int i = 1; i < candidates.Count; i++)
        {
            if (IsBetterAttackCandidate(enabledFeatures, candidates[i], candidates[bestIndex]))
            {
                bestIndex = i;
            }
        }

        return candidates[bestIndex].target;
    }

    public static bool TryChooseMoveDestination(
        AILocalDecisionFeatures enabledFeatures,
        IList<MoveCandidate> candidates,
        out Vector3 chosenDestination)
    {
        chosenDestination = default;
        if (candidates == null || candidates.Count == 0)
        {
            return false;
        }

        bool allowOnlySafeCandidates = false;
        if (HasFeature(enabledFeatures, AILocalDecisionFeatures.DefensiveVeto))
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].immediatelyLosesKeyCity)
                {
                    allowOnlySafeCandidates = true;
                    break;
                }
            }
        }

        int bestIndex = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            MoveCandidate candidate = candidates[i];
            if (allowOnlySafeCandidates && candidate.immediatelyLosesKeyCity)
            {
                continue;
            }

            if (bestIndex < 0 || IsBetterMoveCandidate(candidate, candidates[bestIndex]))
            {
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            return false;
        }

        chosenDestination = candidates[bestIndex].position;
        return true;
    }

    private static void AppendFeatureToken(
        StringBuilder builder,
        AILocalDecisionFeatures enabledFeatures,
        AILocalDecisionFeatures feature,
        string token)
    {
        if (!HasFeature(enabledFeatures, feature))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append('+');
        }

        builder.Append(token);
    }

    private static bool IsBetterImmediateCityWinCandidate(
        ImmediateCityWinCandidate candidate,
        ImmediateCityWinCandidate currentBest)
    {
        if (candidate.requiresAttack != currentBest.requiresAttack)
        {
            return !candidate.requiresAttack;
        }

        if (candidate.targetX != currentBest.targetX)
        {
            return candidate.targetX < currentBest.targetX;
        }

        return candidate.targetY < currentBest.targetY;
    }

    private static bool IsBetterAttackCandidate(
        AILocalDecisionFeatures enabledFeatures,
        AttackCandidate candidate,
        AttackCandidate currentBest)
    {
        if (HasFeature(enabledFeatures, AILocalDecisionFeatures.ExchangeScoring))
        {
            int candidateScore = ComputeExchangeAttackScore(candidate);
            int bestScore = ComputeExchangeAttackScore(currentBest);
            if (candidateScore != bestScore)
            {
                return candidateScore > bestScore;
            }
        }

        return IsBetterBaselineAttackCandidate(candidate, currentBest);
    }

    private static bool IsBetterBaselineAttackCandidate(AttackCandidate candidate, AttackCandidate currentBest)
    {
        if (candidate.canKill != currentBest.canKill)
        {
            return candidate.canKill;
        }

        if (candidate.baselineDistance != currentBest.baselineDistance)
        {
            return candidate.baselineDistance < currentBest.baselineDistance;
        }

        if (candidate.baselineTargetHealth != currentBest.baselineTargetHealth)
        {
            return candidate.baselineTargetHealth < currentBest.baselineTargetHealth;
        }

        return false;
    }

    private static int ComputeExchangeAttackScore(AttackCandidate candidate)
    {
        int score = candidate.predictedDamage * 100;
        if (candidate.canKill)
        {
            score += 1000;
        }

        if (candidate.targetOccupiesEnemyCity)
        {
            score += 250;
        }

        score += candidate.targetAttackUnits * 10;
        return score;
    }

    private static bool IsBetterMoveCandidate(MoveCandidate candidate, MoveCandidate currentBest)
    {
        int distanceComparison = candidate.baselineDistanceToGoal.CompareTo(currentBest.baselineDistanceToGoal);
        if (distanceComparison != 0)
        {
            return distanceComparison < 0;
        }

        if (candidate.targetX != currentBest.targetX)
        {
            return candidate.targetX < currentBest.targetX;
        }

        return candidate.targetY < currentBest.targetY;
    }
}
