using BlockNations.AI;
using BlockNations.Simulation;
using BlockNations.Training;

static class Checks
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        var rules = MatchRuleProfile.CoreThree;
        var normal = new SelfPlayArena(0,7,73,false,2,rules.PolicyCatalog,ruleProfile:rules);
        var explicitFog = new SelfPlayArena(0,7,73,false,2,rules.PolicyCatalog,ruleProfile:rules,diagnosticFullVisionSeats:0);
        for (int i=0;i<200;i++)
        {
            var a=normal.Decision(); var b=explicitFog.Decision();
            Require(a.observation.SequenceEqual(b.observation) && a.available.SequenceEqual(b.available),"Default vision changed.");
            int choice=a.available[i%a.available.Length];
            var terminal=normal.Advance(choice,null); explicitFog.Advance(choice,null);
            if (terminal.Any(t=>t.terminal)) { normal.Advance(null,null); explicitFog.Advance(null,null); }
        }
#if BLOCKNATIONS_TRAINING_DIAGNOSTICS
        for (int seat=0;seat<2;seat++)
        {
            var state=new MatchState(7,7,2,rules.PolicyCatalog,seat,firstSeat:seat,ruleProfile:rules);
            state.AddCity(new SimulationCity(1,seat,0));
            state.AddCity(new SimulationCity(2,1-seat,48));
            state.AddUnit(new SimulationUnit(1,seat,0,state.RecruitType("warrior")));
            state.AddUnit(new SimulationUnit(2,1-seat,48,state.RecruitType("warrior")));
            var observer=new SimulationObservationSource();
            Require(observer.Observe(state,seat).Observation.Units.Length==1,"Fog leaked a distant unit.");
            var full=observer.ObserveWithDiagnosticFullVision(state,seat).Observation;
            Require(full.Units.Length==2 && full.Visible.All(v=>v) && full.Seen.All(v=>v),"No-fog input did not reveal the board.");
            Require(!full.LegalActions.Any(a=>a.Kind==AIActionKind.Attack),"Full vision changed attack range.");
            float[] encoded=LearnedObservationContract.Encode(2,full,-1,state.FirstSeat);
            state.SetGold(1-seat,999); state.GetUnit(2).MovesUsed=1; state.GetUnit(2).AttacksUsed=1;
            var privateChange=observer.ObserveWithDiagnosticFullVision(state,seat).Observation;
            Require(encoded.SequenceEqual(LearnedObservationContract.Encode(2,privateChange,-1,state.FirstSeat)),"No-fog diagnostic exposed enemy private state.");
            // Knowledge is seat-local; revealing seat A does not reveal seat B.
            Require(observer.Observe(state,1-seat).Observation.Units.Length==1,"Diagnostic leaked across seats.");
            var fullArena=new SelfPlayArena(0,7,73,false,2,rules.PolicyCatalog,ruleProfile:rules,diagnosticFullVisionSeats:3);
            for(int i=0;i<200;i++)
            {
                var decision=fullArena.Decision();
                Require(decision.observation.Length==3120,"No-fog changed the model schema.");
                var terminal=fullArena.Advance(decision.available[i%decision.available.Length],null);
                if(terminal.Any(t=>t.terminal))fullArena.Advance(null,null);
            }
            // A visible blocker changes paths, but ownership validation remains authoritative.
            MatchState BlockedFixture()
            {
                var fixture=new MatchState(7,7,2,rules.PolicyCatalog,seat,firstSeat:seat,ruleProfile:rules);
                fixture.AddUnit(new SimulationUnit(1,seat,0,fixture.RecruitType("rider")));
                fixture.AddUnit(new SimulationUnit(2,1-seat,2,fixture.RecruitType("warrior")));
                return fixture;
            }
            var blocked=BlockedFixture();
            var context=observer.ObserveWithDiagnosticFullVision(blocked,seat);
            Require(!context.Commands.Values.Any(c=>c.Kind==MatchActionKind.Move&&c.Destination==2),"Known enemy blocker was traversable.");
            foreach(var command in context.Commands.Values)
                Require(MatchEngine.ApplyWithDiagnosticFullVision(BlockedFixture(),command).Applied,"A diagnostic legal command was rejected.");
            Require(!MatchEngine.ApplyWithDiagnosticFullVision(blocked,new MatchCommand(MatchActionKind.EndTurn,1-seat)).Applied,"Full vision granted another seat's turn.");
        }
        Console.WriteLine("Passed default parity, both-seat full vision, privacy, schema, range, paths and authoritative execution checks.");
#else
        try { new SelfPlayArena(0,7,73,false,2,rules.PolicyCatalog,ruleProfile:rules,diagnosticFullVisionSeats:3); }
        catch(ArgumentException) { Console.WriteLine("Passed ordinary-build fog parity and rejection of full-vision diagnostics."); return; }
        throw new Exception("Product build accepted diagnostic full vision.");
#endif
    }
}
