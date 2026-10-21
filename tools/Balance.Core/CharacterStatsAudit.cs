using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Content.Server.Activities;
using Content.Server.Data;
using Content.Server.Progression;
using Content.Server.Stats;

namespace ProjectG.Balance;

public sealed record AuditBuild(string Id,int[] Weights);
public sealed record AuditGear(string Id,string[] Items);
public sealed record AuditModifier(string Id,DerivedStatModifiers Value);
public sealed record CharacterAuditCases(int Version,int[] Levels,int[] StressLevels,string TargetId,string WeaponId,
    double ReferenceHit,AuditBuild[] Builds,AuditGear[] Gear,AuditModifier[] Modifiers,PowerExperienceCurveDefinition ExperimentalCurve,
    string RangedWeaponId="training_bow",string MagicWeaponId="training_staff");
public sealed record AuditBudget(string Ability,double ManaCost,double StaminaCost,double? ManaUses,double? StaminaUses,double HealthCost);
public sealed record AuditRow(int Level,bool Stress,string Build,string Gear,string Modifier,int Points,BaseStats Primary,
    DerivedStats Stats,double PhysicalMitigation,double MagicMitigation,double PhysicalEffectiveHealth,double MagicEffectiveHealth,
    double BasicDps,double? StationaryTtk,double RangedBasicDps,double MagicBasicDps,IReadOnlyList<AuditBudget> Budgets);
public sealed record AuditActivity(string Id,int Experience,bool Repeatable);
public sealed record AuditCurve(string Id,long TotalToCap,IReadOnlyList<int> Thresholds,IReadOnlyDictionary<string,long> ActivityCounts);
public sealed record CharacterAuditReport(int Version,string ContentHash,string CasesHash,int AlphaCap,string DefenseModel,
    IReadOnlyList<AuditRow> Rows,IReadOnlyList<AuditActivity> Activities,IReadOnlyList<AuditCurve> Curves,string[] Limitations);

/// <summary>Offline comparison using authoritative stat/weapon/defense calculations. Never changes a catalog or save.</summary>
public static class CharacterStatsAudit
{
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    public static CharacterAuditCases LoadCases(string path)
    {
        if(new FileInfo(path).Length>65536)throw new InvalidDataException("Oversized audit fixture.");
        return JsonSerializer.Deserialize<CharacterAuditCases>(File.ReadAllBytes(path),Json)??throw new InvalidDataException("Missing audit fixture.");
    }
    public static CharacterAuditReport Run(ContentCatalog catalog,ExperienceRewardsDefinition rewards,CampServicesDefinition services,
        CharacterAuditCases cases,string contentHash,string casesHash)
    {
        rewards.Validate();services.Validate(catalog);Validate(catalog,cases);
        var calculator=new StatCalculator(catalog.Balance);var basis=catalog.Creatures["test_adventurer"];
        var target=calculator.Calculate(catalog.Creatures[cases.TargetId]);var weapon=catalog.Weapons[cases.WeaponId];
        var rows=new List<AuditRow>();
        foreach(var level in cases.Levels.Concat(cases.StressLevels))
        foreach(var build in cases.Builds)
        {
            var points=catalog.Progression.GrantedPoints(level);var allocation=Allocate(points,build.Weights);
            var primary=new BaseStats(basis.Stats.Strength+allocation[0],basis.Stats.Agility+allocation[1],basis.Stats.Vitality+allocation[2],
                basis.Stats.Intelligence+allocation[3],basis.Stats.Dexterity+allocation[4],basis.Stats.Luck+allocation[5],basis.Stats.Wisdom+allocation[6]);
            foreach(var gear in cases.Gear)
            foreach(var modifier in cases.Modifiers)
            {
                var stats=calculator.Calculate(basis with {Stats=primary});
                foreach(var item in gear.Items)stats=catalog.Items[item].Modifiers.Apply(stats);
                stats=modifier.Value.Apply(stats);
                double Dps(WeaponDefinition w)
                {
                    var hit=calculator.ApplyDefense(calculator.WeaponPower(w,stats),w.Kind==WeaponKind.Magic?target.MagicDefense:target.PhysicalDefense);
                    return StatCalculator.HitProbability(stats.Accuracy,target.Evasion)*hit*
                        (1+StatCalculator.CriticalProbability(stats)*(Math.Max(0,stats.CriticalDamage)-1))/Math.Max(.05,calculator.AttackInterval(w.AttackIntervalSeconds,stats));
                }
                var dps=Dps(weapon);
                var physical=calculator.ApplyDefense(cases.ReferenceHit,stats.PhysicalDefense)/cases.ReferenceHit;
                var magic=calculator.ApplyDefense(cases.ReferenceHit,stats.MagicDefense)/cases.ReferenceHit;
                var budgets=catalog.Abilities.Values.Where(a=>a.ManaCost>0||a.StaminaCost>0||a.HealthCost>0).OrderBy(a=>a.Id,StringComparer.Ordinal)
                    .Select(a=>new AuditBudget(a.Id,a.ManaCost,a.StaminaCost,a.ManaCost>0?Math.Floor(Math.Max(0,stats.MaxMana)/a.ManaCost):null,
                        a.StaminaCost>0?Math.Floor(Math.Max(0,stats.MaxStamina)/a.StaminaCost):null,a.HealthCost)).ToArray();
                rows.Add(new(level,level>catalog.Progression.LevelCap,build.Id,gear.Id,modifier.Id,points,primary,stats,
                    1-physical,1-magic,stats.MaxHealth/physical,stats.MaxHealth/magic,dps,dps>0?target.MaxHealth/dps:null,
                    Dps(catalog.Weapons[cases.RangedWeaponId]),Dps(catalog.Weapons[cases.MagicWeaponId]),budgets));
            }
        }
        var activities=new List<AuditActivity>{new("pve_pool_solo",rewards.PveExperience,true),new("exploration_batch_"+rewards.ExplorationCells,rewards.ExplorationExperience,true),
            new("npc_ore_unit",rewards.OreExperience,true),new("watch_bonus_pool_solo",rewards.WatchExperience,true),new("delivery_"+services.DeliveryId,services.Experience,true),
            new("discovery",catalog.Progression.DiscoveryExperience,false),new("original_quest",catalog.DeliveryQuest!.Experience,false)};
        activities.AddRange(catalog.Crafting!.Recipes.Select(r=>new AuditActivity("recipe_"+r.Id,r.Experience,true)));
        AuditCurve Curve(string id,ExperienceCurve curve)
        {
            var thresholds=Enumerable.Range(1,curve.LevelCap).Select(curve.NextLevel).ToArray();var total=thresholds.Sum(v=>(long)v);
            return new(id,total,thresholds,activities.Where(a=>a.Experience>0).ToDictionary(a=>a.Id,a=>(total+a.Experience-1)/a.Experience));
        }
        var curves=new[]{Curve("current:"+catalog.ExperienceCurve.Key,catalog.ExperienceCurve),
            Curve($"experimental:A={cases.ExperimentalCurve.Scale},B={cases.ExperimentalCurve.Offset}",new(catalog.Progression with {ExperienceCurve=cases.ExperimentalCurve}))};
        return new(1,contentHash,casesHash,catalog.Progression.LevelCap,"Current server ApplyDefense; no sqrt substitution",rows,activities,curves,
            ["Stationary geometric contact assumed; expected DPS includes numeric hit/crit, no active defense, rotation, regeneration, movement, net latency or profession bonuses.",
             "Basic DPS uses the named melee/ranged/magic reference weapons for all rows; gear applies only named item modifiers. Magic uses magic defense, other weapons physical defense. All items are full condition, without enhancement.",
             "Budgets count only upfront costs from full capacity, without regeneration, channel drain, charges or cooldown timing. HP cost is absolute HP; fatal-price permission is not simulated.",
             "Stress levels are offline arithmetic only; alpha cap and saved characters are unchanged.",
             "Activity counts are mathematical equivalents, not hours or repeatability guarantees: discoveries/old quest are once, watch is only a bonus, group pools split, exploration needs new cells, craft needs material.",
             "The case-file power curve is an explicit experiment; no default or curve has been applied. Fixture signed modifiers are test inputs, not new equipment or permanent balance."]);
    }
    public static int[] Allocate(int points,int[] weights)
    {
        if(points<0||weights.Length!=7||weights.Any(w=>w<0||w>100)||weights.Sum()==0)throw new ArgumentException("Invalid allocation weights.");
        var total=weights.Sum();var result=weights.Select(w=>(int)((long)points*w/total)).ToArray();
        // Largest remainder; stable stat order resolves equal remainders. Every point is spent exactly once.
        var order=Enumerable.Range(0,7).OrderByDescending(i=>(long)points*weights[i]%total).ThenBy(i=>i).ToArray();
        var remaining=points-result.Sum();for(var i=0;i<remaining;i++)result[order[i]]++;
        return result;
    }
    private static void Validate(ContentCatalog catalog,CharacterAuditCases c)
    {
        if(c.Version!=1||c.Levels is not {Length:>0 and <=16}||c.StressLevels is not {Length:<=16}||c.Levels.Any(l=>l<1||l>catalog.Progression.LevelCap)||
            c.StressLevels.Any(l=>l<=catalog.Progression.LevelCap||l>1000)||!double.IsFinite(c.ReferenceHit)||c.ReferenceHit<=0||
            !catalog.Creatures.ContainsKey(c.TargetId)||!catalog.Weapons.ContainsKey(c.WeaponId)||!catalog.Weapons.ContainsKey(c.RangedWeaponId)||!catalog.Weapons.ContainsKey(c.MagicWeaponId)||c.Builds is not {Length:>0 and <=32}||
            c.Gear is not {Length:>0 and <=16}||c.Modifiers is not {Length:>0 and <=16}||c.ExperimentalCurve is null)throw new InvalidDataException("Invalid audit cases.");
        foreach(var b in c.Builds){if(string.IsNullOrWhiteSpace(b.Id))throw new InvalidDataException("Missing build ID.");Allocate(18,b.Weights);}
        foreach(var g in c.Gear)if(g.Items is null||g.Items.Length>8||g.Items.Distinct().Count()!=g.Items.Length||g.Items.Any(i=>!catalog.Items.ContainsKey(i)))throw new InvalidDataException("Invalid audit gear.");
        foreach(var m in c.Modifiers)if(m.Value?.IsValid()!=true)throw new InvalidDataException("Invalid audit modifiers.");
    }
    public static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public static void Export(CharacterAuditReport report,string directory)
    {
        Directory.CreateDirectory(directory);BalanceDocument.WriteAtomic(Path.Combine(directory,"report.json"),JsonSerializer.SerializeToUtf8Bytes(report,Json));
        var csv=new StringBuilder("level,stress,build,gear,modifier,points,hp,mp,stamina,physical_mitigation,magic_mitigation,physical_ehp,magic_ehp,basic_dps,stationary_ttk,ranged_basic_dps,magic_basic_dps\n");
        foreach(var r in report.Rows)
        {
            static string Q(string s)=>"\""+s.Replace("\"","\"\"")+"\"";
            csv.AppendLine(string.Join(",",new object[]{r.Level,r.Stress,Q(r.Build),Q(r.Gear),Q(r.Modifier),r.Points,
                F(r.Stats.MaxHealth),F(r.Stats.MaxMana),F(r.Stats.MaxStamina),F(r.PhysicalMitigation),F(r.MagicMitigation),F(r.PhysicalEffectiveHealth),F(r.MagicEffectiveHealth),F(r.BasicDps),r.StationaryTtk is {} t?F(t):"",F(r.RangedBasicDps),F(r.MagicBasicDps)}));
        }
        BalanceDocument.WriteAtomic(Path.Combine(directory,"builds.csv"),Encoding.UTF8.GetBytes(csv.ToString()));
    }
    private static string F(double v)=>v.ToString("R",CultureInfo.InvariantCulture);
}
