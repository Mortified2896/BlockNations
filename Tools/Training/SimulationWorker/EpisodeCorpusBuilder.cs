using System.Security.Cryptography;
using System.Text.Json;
using BlockNations.AI;
using BlockNations.Simulation;
using BlockNations.Training;

// This authoring tool may inspect an owned training replay. The resulting
// curriculum contains reachable command prefixes; policy history is rebuilt
// from each seat's fair observations, never copied from spectator tensors.
internal static class EpisodeCorpusBuilder
{
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    static string Signature(TrainingTraceState f) => JsonSerializer.Serialize(new {
        f.width,f.height,f.seat,f.firstSeat,f.round,f.cityVision,f.incomePerCity,f.gameOver,f.winner,f.tiles,f.gold,
        cities=f.cities.OrderBy(c=>c.id).Select(c=>new{c.id,c.seat,c.position,c.recruited}),
        units=f.units.OrderBy(u=>u.id).Select(u=>new{u.id,u.seat,u.position,u.type,u.health,u.movesUsed,u.attacksUsed,u.surprisedRound})});
    static MatchState Opening(int first)
    {
        var rules=MatchRuleProfile.CoreThree;var state=new MatchState(7,7,2,rules.PolicyCatalog,first,firstSeat:first,ruleProfile:rules);
        for(int seat=0;seat<2;seat++){int p=seat==0?8:40;state.AddCity(new SimulationCity(seat+1,seat,p));state.SetGold(seat,MatchOpening.GoldBeforeIncome(seat,first));}
        MatchEngine.BeginTurn(state,first);return state;
    }
    public static void Write(string input,string output)
    {
        if(File.Exists(output)||new FileInfo(input).Length>16*1024*1024)throw new ArgumentException("Use a new bounded corpus destination.");
        var traces=JsonSerializer.Deserialize<TrainingTrace[]>(File.ReadAllText(input),Json)??throw new ArgumentException("Missing training replays.");
        if(traces.Length<1||traces.Length>32)throw new ArgumentException("Use 1 to 32 training replays.");
        var cases=new List<EpisodeStartCase>();var seen=new HashSet<string>();
        foreach(var trace in traces)
        {
            if(trace.version!=1||trace.rulesVersion!=MatchRuleProfile.CoreThreeVersion||trace.boardSize!=7||trace.firstSeat<0||trace.firstSeat>1||trace.truncated||
                trace.frames==null||trace.frames.Count<3||trace.frames.Count>256||!trace.frames[^1].gameOver||!trace.frames[^1].terminal||trace.frames[^1].winner<0||trace.frames[^1].winner>1)
                throw new ArgumentException("Use complete, untruncated current-rules city-capture training games.");
            foreach(var frame in trace.frames)
                if(frame==null||frame.cities==null||frame.units==null||frame.tiles?.Length!=49||frame.gold?.Length!=2||frame.units.Length>49||frame.cities.Length>49)
                    throw new ArgumentException("Invalid bounded replay frame.");
            var state=Opening(trace.firstSeat);if(Signature(TrainingTraceState.Capture(state,null))!=Signature(trace.frames[0]))throw new ArgumentException("Replay did not start from the actual standard opening.");
            var prefix=new List<EpisodeStartCommand>();var observer=new SimulationObservationSource();
            observer.SetPublicStartingCities(state.Cities.Select(c=>new AICityState{Seat=c.Seat,X=c.Position%7,Y=c.Position/7}).ToArray());
            for(int i=0;i<trace.frames.Count-1;i++)
            {
                var context=observer.Observe(state,state.CurrentTurnSeat);MatchCommand? chosen=null;MatchState next=null;
                foreach(var command in context.Commands.Values)
                {
                    var copy=state.Copy();if(MatchEngine.Apply(copy,command).Applied&&Signature(TrainingTraceState.Capture(copy,null))==Signature(trace.frames[i+1])){chosen=command;next=copy;break;}
                }
                if(chosen==null)throw new ArgumentException("Replay contains a non-legal or altered transition.");
                // Several distinct pre-loss states, still on the losing seat's
                // turn. No asserted optimal defense or named-unit curriculum.
                if(i>=3&&i>=trace.frames.Count-18&&i%3==0&&state.CurrentTurnSeat!=trace.frames[^1].winner&&cases.Count<256)
                {
                    string id=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Signature(trace.frames[i])))).ToLowerInvariant();
                    if(seen.Add(id))cases.Add(new EpisodeStartCase{id=id,firstSeat=trace.firstSeat,commands=prefix.ToArray()});
                }
                var c=chosen.Value;prefix.Add(new EpisodeStartCommand{kind=(int)c.Kind,seat=c.Seat,actorId=c.ActorId,destination=c.Destination,targetId=c.TargetId,recruitType=c.RecruitType});state=next;
            }
        }
        if(cases.Count==0)throw new ArgumentException("No suitable pre-loss training starts were found.");
        var recipe=new EpisodeStartRecipe{version=1,boardSize=7,rulesVersion=MatchRuleProfile.CoreThreeVersion,openingEconomyVersion=2,sourcePartition="training",probability=.15,cases=cases.ToArray()};
        recipe.Validate(7,recipe.rulesVersion,2);
        string contents=JsonSerializer.Serialize(recipe,Json);
        if(System.Text.Encoding.UTF8.GetByteCount(contents)>4*1024*1024)throw new ArgumentException("Generated corpus exceeds the worker bound.");
        using(var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write))
        using(var writer=new StreamWriter(file))writer.Write(contents);
        Console.WriteLine(JsonSerializer.Serialize(new{cases=cases.Count,verifiedGames=traces.Length,probability=.15,partition="training"}));
    }
}
