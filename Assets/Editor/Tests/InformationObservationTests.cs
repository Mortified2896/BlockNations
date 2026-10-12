using System;
using System.Linq;
using BlockNations.AI;
using BlockNations.Simulation;
using NUnit.Framework;
using UnityEngine;

public sealed class InformationObservationTests
{
    [TestCase(2,3120)] [TestCase(3,3259)] [TestCase(4,4712)]
    public void ReleaseRequiresItsExactObservationAndActionContract(int schema,int size)
    {
        var release=ScriptableObject.CreateInstance<LearnedPolicyRelease>();
        var metadata=new TextAsset(JsonUtility.ToJson(new LearnedPolicyRelease.Manifest { schema=schema,actionSchema=2,
            boardSize=7,observationSize=size,actionCount=259,rulesVersion=MatchRuleProfile.CoreThreeVersion,
            modelVersion="test",probabilityOutput="policy_probabilities" }));
        try
        {
            release.metadata=metadata;Assert.That(release.Validate(7).schema,Is.EqualTo(schema));
            release.metadata=new TextAsset(metadata.text.Replace("\"observationSize\":"+size,"\"observationSize\":1"));
            Assert.Throws<InvalidOperationException>(()=>release.Validate(7));
            UnityEngine.Object.DestroyImmediate(release.metadata);
        }
        finally {UnityEngine.Object.DestroyImmediate(metadata);UnityEngine.Object.DestroyImmediate(release);}
    }

    [TestCase(0)] [TestCase(1)]
    public void HistoryCannotReadHiddenWalletPiecesOrActionBudgets(int seat)
    {
        var rules=MatchRuleProfile.CoreThree;var state=new MatchState(7,7,2,rules.PolicyCatalog,seat,firstSeat:seat,ruleProfile:rules);
        state.AddUnit(new SimulationUnit(1,seat,0,state.RecruitType("warrior")));
        var source=new SimulationObservationSource();var history=new FairEnemyHistory();
        var initial=LearnedObservationContract.Encode(4,source.Observe(state,seat).Observation,-1,seat,history);
        state.SetGold(1-seat,999);state.AddUnit(new SimulationUnit(2,1-seat,48,state.RecruitType("archer"),movesUsed:1,attacksUsed:1));
        Assert.That(LearnedObservationContract.Encode(4,source.Observe(state,seat).Observation,-1,seat,history),Is.EqualTo(initial));
        history.Reset();Assert.That(history.Sightings.Count(),Is.EqualTo(0));
    }
}
