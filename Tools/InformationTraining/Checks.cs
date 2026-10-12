using BlockNations.AI;
using BlockNations.Simulation;
using BlockNations.Training;

static class Checks
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected rejection."); }
    static void Main()
    {
        foreach (int size in new[] { 5, 6, 7, 9, 11 })
        {
            var old = new SelfPlayArena(0,size,42,false,2,MatchRuleProfile.CoreThree.PolicyCatalog,ruleProfile:MatchRuleProfile.CoreThree);
            var modern = new SelfPlayArena(0,size,42,false,2,MatchRuleProfile.CoreThree.PolicyCatalog,ruleProfile:MatchRuleProfile.CoreThree,observationVersion:3);
            var memory = new SelfPlayArena(0,size,42,false,2,MatchRuleProfile.CoreThree.PolicyCatalog,ruleProfile:MatchRuleProfile.CoreThree,observationVersion:4);
            for (int i=0;i<128;i++)
            {
                var a=old.Decision();var b=modern.Decision();var c=memory.Decision();
                Require(a.observation.SequenceEqual(b.observation.Take(3120)),"Changed v2 prefix.");
                Require(b.observation.SequenceEqual(c.observation.Take(3259)),"Changed v3 prefix.");
                Require(a.available.SequenceEqual(b.available)&&b.available.SequenceEqual(c.available),"Changed action meanings or legal masks.");
                int choice=a.available[i%a.available.Length];
                var result=old.Advance(choice,null); modern.Advance(choice,null); memory.Advance(choice,null);
                if(result.Any(x=>x.terminal)){old.Advance(null,null);modern.Advance(null,null);memory.Advance(null,null);}
            }
        }
        for(int seat=0;seat<2;seat++)
        {
            var rules=MatchRuleProfile.CoreThree;
            var state=new MatchState(7,7,2,rules.PolicyCatalog,seat,firstSeat:seat,ruleProfile:rules);
            state.AddUnit(new SimulationUnit(1,seat,24,state.RecruitType("rider")));
            state.AddUnit(new SimulationUnit(2,1-seat,25,state.RecruitType("warrior")));
            var observer=new SimulationObservationSource();var history=new FairEnemyHistory();
            var visible=observer.Observe(state,seat).Observation;
            var initial=LearnedObservationContract.Encode(4,visible,-1,state.FirstSeat,history);
            state=new MatchState(7,7,2,rules.PolicyCatalog,seat,round:2,firstSeat:seat,ruleProfile:rules);
            state.AddUnit(new SimulationUnit(1,seat,0,state.RecruitType("rider")));
            state.AddUnit(new SimulationUnit(2,1-seat,47,state.RecruitType("warrior")));
            var hidden=observer.Observe(state,seat).Observation;
            var remembered=LearnedObservationContract.Encode(4,hidden,-1,state.FirstSeat,history);
            int tile=LearnedActionSchema.CanonicalPosition(LearnedActionSchema.CanvasPosition(hidden,25),seat);
            Require(remembered[3259+tile*12]==1 && remembered[3259+tile*12+11]==.01f,"History did not retain an aged legitimate sighting.");
            state.SetGold(1-seat,999);
            var unchanged=LearnedObservationContract.Encode(4,observer.Observe(state,seat).Observation,-1,state.FirstSeat,history);
            Require(remembered.SequenceEqual(unchanged),"Enemy wallet leaked.");
            var clear=new MatchState(7,7,2,rules.PolicyCatalog,seat,round:3,firstSeat:seat,ruleProfile:rules);
            clear.AddUnit(new SimulationUnit(1,seat,24,clear.RecruitType("rider")));
            var empty=LearnedObservationContract.Encode(4,observer.Observe(clear,seat).Observation,-1,clear.FirstSeat,history);
            Require(empty[3259+tile*12]==0,"Reobserved empty tile did not clear a stale sighting.");
            history.Reset();Require(!history.Sightings.Any(),"History survived reset.");
            visible.Units[Array.FindIndex(visible.Units,u=>u.Seat!=seat)].MovesUsed=1;
            Reject(()=>history.Observe(visible,seat));
        }
        var recipe=new EpisodeStartRecipe { version=1,boardSize=7,openingEconomyVersion=2,rulesVersion=MatchRuleProfile.CoreThreeVersion,sourcePartition="training",probability=.15,
            cases=new[]{new EpisodeStartCase {id="legal-opening",firstSeat=0,commands=new[]{new EpisodeStartCommand{kind=2,seat=0,actorId=1,destination=8,recruitType="warrior"}}}}};
        recipe.Validate(7,MatchRuleProfile.CoreThreeVersion,2);
        var r=new SelfPlayArena(0,7,42,false,2,MatchRuleProfile.CoreThree.PolicyCatalog,ruleProfile:MatchRuleProfile.CoreThree,observationVersion:4,episodeStarts:recipe);
        bool selected=false;
        for(int i=0;i<100;i++){r.Reset(); if(r.EpisodeStartId!=null){selected=true;Require(!r.FullOpening && r.RoundLimit==100,"Curriculum must not masquerade as standard-opening Elo or change the round guard.");Require(r.State.GetUnit(1)?.Definition.TypeId=="warrior","Reachable command replay failed.");}}
        Require(selected,"Episode starts were never sampled.");
        recipe.sourcePartition="acceptance";Reject(()=>recipe.Validate(7,MatchRuleProfile.CoreThreeVersion,2));
        recipe.sourcePartition="training";recipe.probability=.3;Reject(()=>recipe.Validate(7,MatchRuleProfile.CoreThreeVersion,2));
        Console.WriteLine("Passed shared-contract prefix/action parity, fair history, both seats, clearing, reset, and reachable-curriculum checks.");
    }
}
