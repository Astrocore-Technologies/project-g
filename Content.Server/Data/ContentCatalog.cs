using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server.Stats;

namespace Content.Server.Data;

/// <summary>Validated server-only snapshot; never send the catalog to clients.</summary>
public sealed class ContentCatalog
{
    public Content.Server.Combat.DefenseBalance Defense { get; }
    public Content.Server.Combat.ControlBalance Control { get; }
    public Content.Server.Combat.CombatLifecycleBalance Lifecycle { get; }
    public Content.Server.Professions.SwordsmanDefinition? Swordsman { get; }
    public Content.Server.WorldStory.WorldNodeDefinition? WorldNode { get; }
    public Content.Server.StarterZone.StarterZoneDefinition? StarterZone { get; }
    public Content.Server.Crafting.CraftingDefinition? Crafting { get; }
    public Content.Server.Quests.DeliveryQuestDefinition? DeliveryQuest {get;}
    public Content.Server.Pvp.PvpDefinition? Pvp {get;}
    public Content.Server.Economy.EconomyDefinition? Economy { get; }
    public const int SchemaVersion = 24;
    public WeaponDefinition UnarmedWeapon { get; }
    public const int MaxFileBytes = 4 * 1024 * 1024;
    private const int MaxDefinitions = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    private ContentCatalog(ContentDocument document)
    {
        if (document.SchemaVersion != SchemaVersion || document.BalanceVersion <= 0)
            throw new ArgumentException("Unsupported schemaVersion or invalid balanceVersion.");
        ArgumentNullException.ThrowIfNull(document.Balance);
        document.Balance.Validate();
        ArgumentNullException.ThrowIfNull(document.Defense); document.Defense.Validate(); Defense=document.Defense;
        ArgumentNullException.ThrowIfNull(document.Control); document.Control.Validate(); Control=document.Control;
        ArgumentNullException.ThrowIfNull(document.Lifecycle); document.Lifecycle.Validate(); Lifecycle=document.Lifecycle;
        ArgumentNullException.ThrowIfNull(document.EchoSummon); document.EchoSummon.Validate(); EchoSummon=document.EchoSummon;
        document.StarterZone?.Validate(); StarterZone=document.StarterZone;
        document.WorldNode?.Validate(); WorldNode = document.WorldNode;
        Balance = document.Balance;
        BalanceVersion = document.BalanceVersion;
        ArgumentNullException.ThrowIfNull(document.Progression); document.Progression.Validate();
        Progression = document.Progression;
        Weapons = Index(document.Weapons, item => item.Id, "weapons");
        if(document.UnarmedWeaponId is null || !Weapons.TryGetValue(document.UnarmedWeaponId,out var unarmed) ||
            unarmed.Kind!=WeaponKind.Melee || unarmed.IsSword || (unarmed.Capabilities&Content.Shared.Combat.ActionCapability.Weapon)!=0)
            throw new ArgumentException("Invalid unarmed weapon profile.");
        UnarmedWeapon=unarmed;
        Abilities = Index(document.Abilities, item => item.Id, "abilities");
        Creatures = Index(document.Creatures, item => item.Id, "creatures");
        Check(document.Echoes is not null && document.Echoes.Length <= MaxDefinitions,"echoes: invalid definition count");
        Echoes = document.Echoes!.Length==0 ? FrozenDictionary<string,EchoCombatDefinition>.Empty : Index(document.Echoes,item=>item.Id,"echoes");
        Items = Index(document.Items, item => item.Id, "items");
        Check(document.Effects is not null && document.Effects.Length <= MaxDefinitions,"effects: invalid definition count");
        Effects = document.Effects!.Length==0 ? FrozenDictionary<string,EffectDefinition>.Empty : Index(document.Effects,item=>item.Id,"effects");
        foreach(var effect in Effects.Values) effect.Validate();
        foreach (var item in Items.Values)
        {
            Check(item.Name is { Length: > 0 and <= 24 } && System.Text.Encoding.UTF8.GetByteCount(item.Name) <= 48,
                $"item {item.Id}: invalid public name");
            Check(Enum.IsDefined(item.Slot) && Enum.IsDefined(item.UseKind) &&
                (item.Slot==Content.Shared.Network.EquipmentSlot.None)==(item.UseKind!=ItemUseKind.None),
                $"item {item.Id}: unknown equipment slot");
            Check(item.Modifiers is not null && item.Modifiers.IsValid(), $"item {item.Id}: invalid modifiers");
            ArgumentNullException.ThrowIfNull(item.ControlResistance);item.ControlResistance.Validate();
            Check(item.UseKind==ItemUseKind.None || item.Modifiers!.IsEmpty && item.Condition is null && item.ControlResistance.IsEmpty,
                $"item {item.Id}: consumable cannot grant equipment modifiers/durability");
            Check(item.Slot == Content.Shared.Network.EquipmentSlot.Weapon
                ? item.WeaponId is not null && Weapons.ContainsKey(item.WeaponId)
                : item.WeaponId is null, $"item {item.Id}: invalid weapon reference/kind");
        }

        ArgumentNullException.ThrowIfNull(document.EchoRecovery); document.EchoRecovery.Validate(this); EchoRecovery=document.EchoRecovery;

        document.Crafting?.Validate(this); Crafting=document.Crafting;
        document.DeliveryQuest?.Validate(this); DeliveryQuest=document.DeliveryQuest;
        foreach(var item in Items.Values) if(item.Condition is { } condition)
        { condition.Validate(); Check(item.Slot is Content.Shared.Network.EquipmentSlot.Weapon or Content.Shared.Network.EquipmentSlot.Offhand && Crafting is not null && Crafting.Materials.Any(m=>m.Id==condition.RepairMaterialId), "Invalid durability material/slot reference."); }
        document.Economy?.Validate(this); Economy=document.Economy; document.Pvp?.Validate(); Pvp=document.Pvp;
        foreach (var weapon in Weapons.Values)
        {
            weapon.HitRules.Validate();
            weapon.HitContext?.Validate($"weapon {weapon.Id}.hitContext");
            Check((weapon.Capabilities&~(Content.Shared.Combat.ActionCapability.Weapon|Content.Shared.Combat.ActionCapability.Magical|
                Content.Shared.Combat.ActionCapability.Movement))==0,$"weapon {weapon.Id}: unknown action capabilities");
            Check(Enum.IsDefined(weapon.Kind), $"weapon {weapon.Id}: unknown kind");
            NonNegative(weapon.Attack, $"weapon {weapon.Id}.attack");
            Positive(weapon.Range, $"weapon {weapon.Id}.range");
            Positive(weapon.AttackIntervalSeconds, $"weapon {weapon.Id}.attackIntervalSeconds");
            weapon.BasicAttack?.Validate($"weapon {weapon.Id}.basicAttack");
            Check((weapon.BasicVolume is not null)!=(weapon.BasicProjectile is not null),$"weapon {weapon.Id}: exactly one basic volume/projectile is required");
            weapon.BasicVolume?.Validate($"weapon {weapon.Id}.basicVolume",weapon.Range);
            weapon.BasicProjectile?.Validate($"weapon {weapon.Id}.basicProjectile");
            Check(weapon.BasicProjectile is null || weapon.BasicAttack is not null,$"weapon {weapon.Id}: projectile basic requires explicit phases");
        }
        var abilityIds = new HashSet<ushort>();
        var defaultSkillCurve = Content.Server.Progression.SkillProgressionDefinition.FromDefaults(Progression);
        var skillCurves = new Dictionary<string, Content.Server.Progression.SkillProgressionDefinition>(StringComparer.Ordinal);
        foreach (var ability in Abilities.Values)
        {
            Check(ability.Kind == AbilityKind.Projectile
                ? ability.ProjectileLaunchHeight is { } launch && float.IsFinite(launch) && launch is >= 0 and <= 128
                : ability.ProjectileLaunchHeight is null,
                $"ability {ability.Id}: projectile requires an explicit finite launch height in [0,128]; other kinds cannot define one");
            Check((ability.Kind==AbilityKind.GroundArea)==(ability.GroundVolume is not null),$"ability {ability.Id}: missing/unexpected groundVolume");
            ability.GroundVolume?.Validate();
            Check((ability.Kind==AbilityKind.Dash)==(ability.Dodge is not null),$"ability {ability.Id}: missing/unexpected dodge profile");
            ability.Dodge?.Validate();
            if(ability.Kind==AbilityKind.Dash)
            {
                Check(ability.Speed==0 && ability.ManaCost==0,$"ability {ability.Id}: dodge speed is derived from range/duration, MP price must be zero");
                ability.AuthoredDodgeProfile().Validate();
            }
            if(ability.Action is null) throw new ArgumentException($"ability {ability.Id}: missing action lifecycle");
            ability.Action.Validate($"ability {ability.Id}.action");
            if(ability.Kind==AbilityKind.Dash)
                Check(ability.Action.Form==Content.Shared.Combat.CombatActionForm.Instant && ability.Action.Category==Content.Shared.Combat.CombatActionCategory.Dodge &&
                    ability.Action.Preparation.Seconds==0 && ability.Action.Active.Seconds==0 && !ability.Action.Active.CanTurn &&
                    (ability.Action.Capabilities&Content.Shared.Combat.ActionCapability.Movement)!=0,
                    $"ability {ability.Id}: dodge requires instant activation, profile-owned duration and fixed direction");
            var passive=ability.Action.Form==Content.Shared.Combat.CombatActionForm.Passive;
            Check(passive==(ability.Passive is not null) && passive==(ability.Kind==AbilityKind.Passive),
                $"ability {ability.Id}: missing, unexpected or inconsistent passive policy");
            if(passive)
                Check(ability.Power==0 && ability.MagicAttackScale==0 && ability.ManaCost==0 && ability.StaminaCost==0 && ability.HealthCost==0 && !ability.AllowFatalHealthCost &&
                    ability.CooldownSeconds==0 && ability.MaxCharges==1 && ability.Effects is {Length:0} &&
                    ability.Range==0 && ability.Radius==0 && ability.Speed==0 && ability.Melee is null &&
                    ability.Action.Preparation.Seconds==0 && ability.Action.Active.Seconds==0 && ability.Action.Recovery.Seconds==0 &&
                    ability.Action.Capabilities==Content.Shared.Combat.ActionCapability.None && ability.ContactDisposition==AbilityContactDisposition.Harmful,
                    $"ability {ability.Id}: passive cannot define an active execution, payment, cooldown or contact payload");
            var channel=ability.Action.Form is Content.Shared.Combat.CombatActionForm.Channel or Content.Shared.Combat.CombatActionForm.Hold or Content.Shared.Combat.CombatActionForm.Toggle;
            var toggle=ability.Action.Form==Content.Shared.Combat.CombatActionForm.Toggle;
            Check(toggle==(ability.Toggle is not null),$"ability {ability.Id}: missing or unexpected toggle policy");
            if(toggle)
            {
                ability.Toggle!.Validate($"ability {ability.Id}.toggle");
                Check(ability.Toggle.OccupiesAction || ability.Action.Recovery.Seconds==0,
                    $"ability {ability.Id}: background toggle recovery cannot own the ordinary lane");
                Check(ability.Action.Interruptible || (ability.Toggle.StopOn&Content.Shared.Combat.ToggleStopTriggers.EnemyInterrupt)==0,
                    $"ability {ability.Id}: conflicting interruption policies");
            }
            var charged=ability.Action.Form==Content.Shared.Combat.CombatActionForm.Charged;
            Check(charged==(ability.Charge is not null),$"ability {ability.Id}: missing or unexpected charge policy");
            if(charged)
            {
                ability.Charge!.Validate($"ability {ability.Id}.charge");
                Check(ability.Kind!=AbilityKind.Dash && ability.Action.Preparation.Seconds==0 && ability.Action.Active.Seconds==0 &&
                    (ability.Action.Preparation.Cancel&Content.Shared.Combat.ActionCancelPermission.Voluntary)!=0,
                    $"ability {ability.Id}: charge owns preparation timing, requires voluntary cancellation and does not support dash");
            }
            Check(channel==(ability.Channel is not null),$"ability {ability.Id}: missing or unexpected channel policy");
            if(channel)
            {
                ability.Channel!.Validate($"ability {ability.Id}.channel");
                Check(ability.Kind is AbilityKind.GroundArea or AbilityKind.Recovery or AbilityKind.Melee or AbilityKind.Projectile && ability.Action.Active.Seconds==0,
                    $"ability {ability.Id}: sustained pulses require an authored contact geometry and a separate channel duration");
            }
            if(ability.Action.Form is Content.Shared.Combat.CombatActionForm.Hold or Content.Shared.Combat.CombatActionForm.Toggle)
                Check((ability.Action.Preparation.Cancel&Content.Shared.Combat.ActionCancelPermission.Voluntary)!=0 &&
                    (ability.Action.Active.Cancel&Content.Shared.Combat.ActionCancelPermission.Voluntary)!=0,
                    $"ability {ability.Id}: Hold requires release cancellation during preparation and active work");
            ability.HitRules.Validate();
            ability.HitContext?.Validate($"ability {ability.Id}.hitContext");
            Check(ability.Effects is not null && ability.Effects.Length<=MaxDefinitions,$"ability {ability.Id}: invalid effects list");
            Check(Enum.IsDefined(ability.ContactDisposition),$"ability {ability.Id}: invalid contact disposition");
            var support=ability.ContactDisposition==AbilityContactDisposition.Support;
            Check(!support || ability.Kind==AbilityKind.GroundArea && ability.Power==0 && ability.MagicAttackScale==0,
                $"ability {ability.Id}: support contact currently requires a ground area without damage");
            var applications=new HashSet<(SkillEffectOperation,string,Content.Shared.Combat.CrowdControlKind?,SkillEffectTrigger,SkillEffectTarget)>();
            foreach(var application in ability.Effects!)
            {
                Check(application is not null && Enum.IsDefined(application.Operation) &&
                    Enum.IsDefined(application.Trigger) && Enum.IsDefined(application.Target) &&
                    (application.Trigger!=SkillEffectTrigger.Activated || application.Target==SkillEffectTarget.Self) &&
                    (!support || application.Trigger is SkillEffectTrigger.Activated or SkillEffectTrigger.Contact) &&
                    applications.Add((application.Operation,application.EffectId??application.ResetSkillId??"",application.Control?.Kind,application.Trigger,application.Target)),$"ability {ability.Id}: invalid effect application");
                Check(Content.Shared.Combat.CombatHitTagRules.IsValid(application!.RequiredHitTags) && HitContextRule.KnownTags(application.ForbiddenHitTags) &&
                    (application.RequiredHitTags&application.ForbiddenHitTags)==0 &&
                    (application.Trigger!=SkillEffectTrigger.Activated || (application.RequiredHitTags|application.ForbiddenHitTags)==0),
                    $"ability {ability.Id}: invalid contact context filter");
                Check((application!.Operation==SkillEffectOperation.Control)==(application.Control is not null),
                    $"ability {ability.Id}: missing or unexpected control payload");
                var removal=application.Operation is SkillEffectOperation.Cleanse or SkillEffectOperation.Dispel;
                Check(removal==(application.Removal is not null),$"ability {ability.Id}: missing or unexpected removal payload");
                Check((application.Operation==SkillEffectOperation.CooldownReset)==(application.ResetSkillId is not null),
                    $"ability {ability.Id}: missing or unexpected cooldown reset target");
                Check((application.Operation==SkillEffectOperation.SelfDamage)==(application.SelfDamageFamily is not null),
                    $"ability {ability.Id}: missing or unexpected self-damage family");
                if(application!.Operation==SkillEffectOperation.Timed)
                {
                    Check(application.EffectId is not null && Effects.ContainsKey(application.EffectId) && application.Power is null,
                        $"ability {ability.Id}: invalid timed effect reference/power");
                    var harmful=Effects[application.EffectId!].Disposition==EffectDisposition.Harmful;
                    Check(!application.AllowOtherPlayers || !harmful,$"ability {ability.Id}: harmful effect cannot grant support permission");
                    Check(!support || !harmful,$"ability {ability.Id}: harmful effect in support area");
                }
                else if(application.Operation==SkillEffectOperation.SelfDamage)
                {
                    Check(application.Target==SkillEffectTarget.Self && application.EffectId is null && application.Power is not null &&
                        !application.AllowOtherPlayers && !application.RequiresBleedable && Enum.IsDefined(application.SelfDamageFamily!.Value),
                        $"ability {ability.Id}: self damage requires explicit self target, family and power");
                    application.Power!.Validate();
                }
                else if(application.Operation==SkillEffectOperation.CooldownReset)
                {
                    Check(application.ResetSkillId is not null && Abilities.TryGetValue(application.ResetSkillId,out var reset) && reset.Passive is null &&
                        application.EffectId is null && application.Power is null && !application.RequiresBleedable && !application.ScalePowerWithSkillLevel &&
                        (application.Target==SkillEffectTarget.Self || support),
                        $"ability {ability.Id}: cooldown reset requires a known active skill and a supported recipient");
                }
                else if(application.Operation==SkillEffectOperation.Control)
                {
                    Check(!support && application.EffectId is null && application.Power is null && !application.RequiresBleedable &&
                        !application.AllowOtherPlayers && !application.ScalePowerWithSkillLevel && application.Target==SkillEffectTarget.HitTarget,
                        $"ability {ability.Id}: control requires a hostile hit target without a separate power or support permission");
                    application.Control!.Validate($"ability {ability.Id}.control");
                }
                else if(application.Operation==SkillEffectOperation.Threat)
                {
                    Check(!support && application.EffectId is null && application.Power is not null && !application.RequiresBleedable &&
                        !application.AllowOtherPlayers && application.Target==SkillEffectTarget.HitTarget,
                        $"ability {ability.Id}: threat requires a hostile hit target and authored power");
                    application.Power!.Validate();
                }
                else if(removal)
                {
                    var cleanse=application.Operation==SkillEffectOperation.Cleanse;
                    Check(application.EffectId is null && application.Power is null && !application.RequiresBleedable &&
                        !application.ScalePowerWithSkillLevel &&
                        (cleanse ? application.Target==SkillEffectTarget.Self || support :
                            !support && application.Target==SkillEffectTarget.HitTarget && !application.AllowOtherPlayers),
                        $"ability {ability.Id}: invalid cleanse/support or hostile dispel target");
                    application.Removal!.Validate(cleanse,$"ability {ability.Id}.removal");
                }
                else
                {
                    Check(application.EffectId is null && application.Power is not null && !application.RequiresBleedable &&
                        (application.Target==SkillEffectTarget.Self || support) &&
                        (application.Operation!=SkillEffectOperation.Resurrect || application.Target==SkillEffectTarget.HitTarget),
                        $"ability {ability.Id}: invalid direct support operation");
                    application.Power!.Validate();
                    Check(application.Power.Flat>0 || application.Power.MeleeScale>0 || application.Power.RangedScale>0 ||
                        application.Power.MagicScale>0 || application.Power.WeaponScale>0,$"ability {ability.Id}: support power must be positive");
                }
            }
            Check(ability.Kind == AbilityKind.Melee || !ability.HitRules.Parryable, $"ability {ability.Id}: only melee attacks may be parried");
            Check(ability.Kind != AbilityKind.GroundArea || !ability.HitRules.Blockable, $"ability {ability.Id}: ground areas cannot be blocked");
            Check(document.SchemaVersion >= 4 || ability.Progression is null, $"ability {ability.Id}: individual progression requires schemaVersion 4");
            var curve = ability.Progression ?? defaultSkillCurve;
            curve.Validate(ability.Id);
            skillCurves.Add(ability.Id, curve);
            Check(ability.NetworkId != 0 && abilityIds.Add(ability.NetworkId), $"ability {ability.Id}: duplicate/zero networkId");
            Check(Enum.IsDefined(ability.Kind), $"ability {ability.Id}: unknown kind");
            NonNegative(ability.Power, $"ability {ability.Id}.power");
            NonNegative(ability.ManaCost, $"ability {ability.Id}.manaCost");
            NonNegative(ability.CastSeconds, $"ability {ability.Id}.castSeconds");
            NonNegative(ability.CooldownSeconds, $"ability {ability.Id}.cooldownSeconds");
            Check(ability.MaxCharges>0,$"ability {ability.Id}: maxCharges must be positive");
            if(!passive){Positive(ability.Range, $"ability {ability.Id}.range");Positive(ability.Radius, $"ability {ability.Id}.radius");}
            NonNegative(ability.Speed, $"ability {ability.Id}.speed");
            Check(ability.Kind!=AbilityKind.Projectile || ability.Speed>0,$"ability {ability.Id}: projectile speed must be positive");
            NonNegative(ability.StaminaCost, $"ability {ability.Id}.staminaCost");
            NonNegative(ability.HealthCost,$"ability {ability.Id}.healthCost");
            Check(!ability.AllowFatalHealthCost || ability.HealthCost>0,$"ability {ability.Id}: fatal payment requires a positive HP price");
            Check((ability.Kind is AbilityKind.Melee or AbilityKind.Recovery) == (ability.Melee is not null), "Missing/unexpected melee technique.");
            ability.Melee?.Validate();
            if(ability.Kind==AbilityKind.Melee && ability.Melee is {} volume)
            {
                Check(volume.OriginHeight.HasValue && (volume.NarrowThrust ? volume.Thickness is null : volume.Thickness.HasValue),
                    $"ability {ability.Id}: melee requires an explicit origin and arc thickness (thrust uses radius)");
                Check(volume.NarrowThrust || volume.ArcDegrees>0,$"ability {ability.Id}: arc requires an explicit positive angle");
                foreach(var weapon in Weapons.Values)
                    if(weapon.IsSword)
                        Check((volume.NarrowThrust?ability.Radius:volume.Thickness!.Value*.5)<=weapon.Range*volume.RangeFactor,
                            $"ability {ability.Id}: attack padding exceeds its weapon range");
            }
            else if(ability.Melee is {} nonAttack)
                Check(nonAttack.OriginHeight is null && nonAttack.Thickness is null,$"ability {ability.Id}: recovery cannot define attack geometry");
            if (ability.Melee is { } technique)
                Check(technique.HitFollowups.All(id => Abilities.TryGetValue(id,out var next) && next.Kind == AbilityKind.Melee),
                    $"ability {ability.Id}: unknown/non-melee followup");
            NonNegative(ability.MagicAttackScale, $"ability {ability.Id}.magicAttackScale");
        }
        SkillProgressions = skillCurves.ToFrozenDictionary(StringComparer.Ordinal);
        SkillProgressionByNetworkId = Abilities.Values.ToFrozenDictionary(a => a.NetworkId, a => skillCurves[a.Id]);
        Check(Abilities.Values.Any(a => a.NetworkId == Progression.DiscoverySkillId && a.Kind != AbilityKind.Dash), "progression: unknown discovery skill");
        if (document.Professions is null || document.Professions.Length > 8) throw new ArgumentException("Invalid profession budget.");
        var professionIds = new HashSet<ushort>();
        foreach (var profession in document.Professions)
            Check(profession is not null && profession.Id != 0 && professionIds.Add(profession.Id) &&
                profession.Name is { Length: > 0 and <= 24 } && System.Text.Encoding.UTF8.GetByteCount(profession.Name) <= 72 &&
                (profession.RequiresTrainer || profession.DiscoveryMask is > 0 and <= 3 && profession.SuccessfulUses is > 0 and <= 10000) &&
                profession.SkillId is not null && Abilities.TryGetValue(profession.SkillId,out var skill) && skill.Kind != AbilityKind.Dash,
                "Invalid profession definition/reference.");
        Professions = Array.AsReadOnly((Content.Server.Professions.ProfessionDefinition[])document.Professions.Clone());
        foreach(var ability in Abilities.Values)ability.Passive?.Validate(this,$"ability {ability.Id}.passive");
        foreach (var profession in Professions)
        {
            profession.Dodge?.Validate();
            Check(profession.AdditionalSkills is not null && profession.AdditionalSkills.Length < Content.Shared.Network.NetworkConstants.MaxLearnedSkills && profession.AllSkills.Distinct().Count() == profession.AdditionalSkills.Length + 1 &&
                profession.AllSkills.All(id => Abilities.TryGetValue(id, out var a) && a.Kind != AbilityKind.Dash), "Invalid profession skill list.");
        }
        document.Swordsman?.Validate(this); Swordsman = document.Swordsman;
        foreach (var creature in Creatures.Values)
        {
            if (creature.Hurtbox is null) throw new ArgumentException($"creature {creature.Id}: missing hurtbox.");
            creature.Hurtbox.Validate($"creature {creature.Id}.hurtbox");
            ArgumentNullException.ThrowIfNull(creature.Avoidance);creature.Avoidance.Validate(creature.Id);
            if(creature.Threat is null)throw new ArgumentException($"creature {creature.Id}: missing threat profile.");
            creature.Threat.Validate($"creature {creature.Id}.threat");
            ArgumentNullException.ThrowIfNull(creature.ControlResistance); creature.ControlResistance.Validate();
            Check(creature.Modifiers is not null && creature.Modifiers.IsValid(),
                $"creature {creature.Id}: derived modifiers must be finite");
            Positive(creature.BaseHealth, $"creature {creature.Id}.baseHealth");
            NonNegative(creature.BaseMana, $"creature {creature.Id}.baseMana");
            NonNegative(creature.BaseHealthRecovery, $"creature {creature.Id}.baseHealthRecovery");
            NonNegative(creature.BaseManaRecovery, $"creature {creature.Id}.baseManaRecovery");
            Check(creature.WeaponId is not null && Weapons.ContainsKey(creature.WeaponId),
                $"creature {creature.Id}: unknown weaponId {creature.WeaponId}");
            Check(!creature.AbilityIds.IsDefault && creature.AbilityIds.Length <= MaxDefinitions,
                $"creature {creature.Id}: invalid abilityIds count");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in creature.AbilityIds)
                Check(id is not null && Abilities.ContainsKey(id) && seen.Add(id),
                    $"creature {creature.Id}: unknown or duplicate abilityId {id}");
            Check(!creature.StarterItemIds.IsDefault && creature.StarterItemIds.Length <= Content.Shared.Network.NetworkConstants.MaxInventoryItems,
                $"creature {creature.Id}: invalid starter inventory");
            foreach (var id in creature.StarterItemIds)
                Check(id is not null && Items.ContainsKey(id), $"creature {creature.Id}: unknown starter item {id}");
        }
        foreach(var echo in Echoes.Values) echo.Validate(this);
    }

    public FrozenDictionary<string,EchoCombatDefinition> Echoes {get;}
    public EchoSummonDefinition EchoSummon {get;}
    public EchoRecoveryDefinition EchoRecovery {get;}
    public Content.Server.Progression.ProgressionBalance Progression { get; }
    public IReadOnlyList<Content.Server.Professions.ProfessionDefinition> Professions { get; }
    public int BalanceVersion { get; }
    public StatBalance Balance { get; }
    public FrozenDictionary<string, WeaponDefinition> Weapons { get; }
    public FrozenDictionary<string, AbilityDefinition> Abilities { get; }
    public FrozenDictionary<string, EffectDefinition> Effects { get; }
    public FrozenDictionary<string, Content.Server.Progression.SkillProgressionDefinition> SkillProgressions { get; }
    public FrozenDictionary<ushort, Content.Server.Progression.SkillProgressionDefinition> SkillProgressionByNetworkId { get; }
    public FrozenDictionary<string, CreatureDefinition> Creatures { get; }
    public FrozenDictionary<string, ItemDefinition> Items { get; }

    public static ContentCatalog LoadFile(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            Check(file.Length is > 0 and <= MaxFileBytes, "content file exceeds size budget or is empty");
            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            return Parse(bytes);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException($"Cannot read content '{path}': {exception.Message}", exception);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"Invalid content '{path}': {exception.Message}", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Invalid content '{path}': {exception.Message}", exception);
        }
    }

    public static ContentCatalog Parse(ReadOnlySpan<byte> utf8)
    {
        try
        {
            Check(utf8.Length is > 0 and <= MaxFileBytes, "content file exceeds size budget or is empty");
            // Duplicate JSON keys otherwise silently overwrite earlier values.
            using var json = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicateProperties(json.RootElement);
            var document = JsonSerializer.Deserialize<ContentDocument>(utf8, JsonOptions)
                ?? throw new ArgumentException("Content document must not be null.");
            return new ContentCatalog(document);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException($"Invalid content: {exception.Message}", exception);
        }
    }

    private static FrozenDictionary<string, T> Index<T>(T[]? values, Func<T, string> getId, string kind)
        where T : class
    {
        Check(values is { Length: > 0 and <= MaxDefinitions }, $"{kind}: invalid definition count");
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in values!)
        {
            Check(item is not null, $"{kind}: null definition");
            var id = getId(item!);
            Check(IsValidId(id), $"{kind}: invalid ID '{id}'");
            Check(result.TryAdd(id, item!), $"{kind}: duplicate ID '{id}'");
        }
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static bool IsValidId(string? id)
    {
        if (id is not { Length: > 0 and <= 64 })
            return false;
        foreach (var value in id)
            if (!(value is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.'))
                return false;
        return true;
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Check(names.Add(property.Name), $"duplicate JSON property '{property.Name}'");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                RejectDuplicateProperties(child);
    }

    private static void Positive(double value, string name) =>
        Check(double.IsFinite(value) && value > 0, $"{name}: expected finite positive value");

    private static void NonNegative(double value, string name) =>
        Check(double.IsFinite(value) && value >= 0, $"{name}: expected finite non-negative value");

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new ArgumentException(message);
    }
}
