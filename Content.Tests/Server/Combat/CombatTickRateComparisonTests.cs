using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Content.Tests.Server.Combat;

/// <summary>A repeatable combat-clock probe, not a whole-server capacity claim.</summary>
public sealed class CombatTickRateComparisonTests(ITestOutputHelper output)
{
#if DEBUG
    private const string BuildConfiguration="Debug";
#else
    private const string BuildConfiguration="Release";
#endif
    private sealed record Cost(int TickRate,int Actors,int Seconds,int Contacts,double TotalMs,
        double P50StepMs,double P95StepMs,double P99StepMs,long AllocatedBytes);
    private sealed record Windows(int TickRate,int RttMs,bool Dodge,int Cases,int Protected,int Mismatches,double MaximumTimingErrorMs);

    [Fact]public void CompareTwentyAndThirtyTpsWithoutRewindingInputsOrChangingDefenseWindows()
    {
        var catalog=ContentCatalogTests.Load();var windows=new List<Windows>();
        foreach(var rate in new[]{30,20})
            foreach(var rtt in new[]{50,100,150})
                foreach(var dodge in new[]{false,true})windows.Add(ProbeWindows(catalog,rate,rtt,dodge));
        Assert.All(windows,row=>{Assert.Equal(0,row.Mismatches);Assert.InRange(row.MaximumTimingErrorMs,0,.002);});
        var artifact=Environment.GetEnvironmentVariable("PROJECT_G_TPS_REPORT");
        // Normal regression checks correctness with a small fixture; explicit measurement uses 128 actors for 30 simulated seconds.
        var measured=!string.IsNullOrEmpty(artifact);var costs=new List<Cost>();
        RunCost(catalog,30,8,2);RunCost(catalog,20,8,2); // JIT warm-up excluded from measurements.
        for(var round=0;round<(measured?3:1);round++)
            foreach(var rate in round%2==0?new[]{30,20}:new[]{20,30})costs.Add(RunCost(catalog,rate,measured?64:8,measured?30:2));
        Assert.Single(costs.Select(row=>row.Contacts).Distinct());
        var report=new {CreatedUtc=DateTime.UtcNow,Runtime=Environment.Version.ToString(),Configuration=BuildConfiguration,
            Scope="Common combat runtime, 64 attacking players + 64 target bodies, continuous input receipts, spatial queries and phased contacts. Excludes transport, database, AOI serialization and render cost.",
            LatencyScope="Deterministic server receipt at press + RTT/2 for 50/100/150 ms RTT; direct post-geometry contacts probe Dodge/Parry windows. No network jitter or packet loss in this probe.",
            Windows=windows,Costs=costs};
        var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});output.WriteLine(json);
        if(measured){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(artifact!))!);File.WriteAllText(artifact!,json);}
    }

    private static Windows ProbeWindows(ContentCatalog catalog,int rate,int rtt,bool dodge)
    {
        var protectedCount=0;var mismatches=0;var maxError=0d;const int cases=24;
        for(var sample=0;sample<cases;sample++)
        {
            var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,
                server:Options.Create(new ServerOptions{TickRate=rate}),hitRoll:()=>0,criticalRoll:()=>1);
            var target=world.AddPlayer(1,new(1),world.CreateInitialCharacter() with {X=-7,Z=0});
            var source=world.AddPlayer(2,new(2),world.CreateInitialCharacter() with {X=-7,Z=1});
            var combat=world.Combat!;combat.DamagePermission=(_,_)=>true;
            const double pressed=.2;var received=pressed+rtt/2000d;
            var contactAt=pressed+.01+sample*.015;
            var command=dodge?new CombatInput(CombatInputKind.Skill,Skill:new(1,0,3,Vector2.UnitX)):
                new CombatInput(CombatInputKind.Defense,Defense:new(1,DefenseAction.Parry,Vector2.UnitY));
            Assert.True(combat.ScheduleInput(target.EntityId,command,received));
            var oldBoundary=combat.MotionBoundary;var oldContacts=combat.ResolveWorldContacts;var done=false;var guarded=false;var observed=0d;
            combat.MotionBoundary=end=>CombatSimulation.EarlierBoundary(combat.Time,oldBoundary?.Invoke(end)??end,contactAt);
            combat.ResolveWorldContacts=tick=>
            {
                oldContacts?.Invoke(tick);if(done||combat.Time<contactAt)return;
                done=true;observed=combat.Time;
                var hit=combat.ResolveDirectHit(source.EntityId,target.EntityId,-Vector2.UnitY,1,false,parryable:true);
                guarded=hit.Guard==(dodge?GuardImpact.Dodged:GuardImpact.Parried);
            };
            while(!done)world.Simulate(1f/rate);
            var expected=contactAt>=received && contactAt<received+(dodge?.15:.25);
            if(guarded)protectedCount++;if(guarded!=expected)mismatches++;
            maxError=Math.Max(maxError,Math.Abs(observed-contactAt)*1000);
        }
        return new(rate,rtt,dodge,cases,protectedCount,mismatches,maxError);
    }

    private static Cost RunCost(ContentCatalog catalog,int rate,int fighters,int seconds)
    {
        var spatial=new SpatialIndex(8);var combat=new CombatSimulation(catalog,spatial,
            new NavigationOptions().CreateGrid(new MovementOptions().ToSettings()),new CombatOptions(),rate,8,()=>1,()=>0);
        for(var i=0;i<fighters;i++)
        {
            var position=new Vector2(-11+(i%8)*3,-11+(i/8)*3);var player=new NetworkEntityId((ulong)i*2+1);var target=new NetworkEntityId((ulong)i*2+2);
            combat.Add(player,position,CombatEntityKind.Player);combat.Add(target,position+Vector2.UnitY,CombatEntityKind.Monster);
            spatial.Add(player,position);spatial.Add(target,position+Vector2.UnitY);
            var body=combat.Get(target);body.BaseDerivedStats=body.Stats=body.Stats with {MaxHealth=1e9,HealthRecovery=0};body.Health=1e9;
        }
        var steps=seconds*rate;var durations=new double[steps];var nextReceipt=.00123;uint sequence=0;var contacts=0;
        var before=GC.GetAllocatedBytesForCurrentThread();var total=Stopwatch.StartNew();
        for(var step=0;step<steps;step++)
        {
            var started=Stopwatch.GetTimestamp();var end=combat.Time+1f/rate;
            while(nextReceipt<=end)
            {
                sequence++;
                for(var i=0;i<fighters;i++)Assert.True(combat.ScheduleInput(new((ulong)i*2+1),new(CombatInputKind.Basic,
                    Attack:new(sequence,(uint)step,Vector2.UnitY,BasicAttackMode.BeginContinuous)),nextReceipt));
                nextReceipt+=.2;
            }
            combat.Simulate(1f/rate,(uint)step+1);contacts+=combat.Events.Count;
            durations[step]=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        total.Stop();var allocated=GC.GetAllocatedBytesForCurrentThread()-before;Array.Sort(durations);
        return new(rate,fighters*2,seconds,contacts,total.Elapsed.TotalMilliseconds,durations[steps/2],durations[(int)(steps*.95)],durations[(int)(steps*.99)],allocated);
    }
}
