using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Data;

public sealed class EchoCombatDefinitionTests
{
    [Fact]
    public void MiraUsesApprovedCommonStatsWeaponAndManualSignature()
    {
        var catalog=ContentCatalogTests.Load();var echo=catalog.Echoes["test_guardian_echo"];
        var profile=catalog.Creatures[echo.ProfileId];var stats=new StatCalculator(catalog.Balance).Calculate(profile);
        Assert.Equal(new BaseStats(10,10,10,10,10,10,10),profile.Stats);
        Assert.Equal(110,stats.MaxHealth,8);Assert.Equal(140,stats.MaxMana);Assert.Equal(100,stats.MaxStamina);
        Assert.Equal(34,stats.MagicAttack);Assert.Equal("training_sword",profile.WeaponId);
        Assert.Equal(5.5f,echo.Speed);Assert.Equal(1.5f,echo.FollowDistance);Assert.Equal(30,echo.TeleportDistance);
        Assert.Equal(3,echo.UnreachableSeconds);Assert.Equal(2,echo.ArrivalRadius);
        var skill=catalog.Abilities[echo.SignatureId];Assert.True(skill.ManualOnly);
        Assert.Equal(0,skill.Power);Assert.Equal(1,skill.MagicAttackScale);Assert.Equal(6,skill.Range);Assert.Equal(2,skill.Radius);
        Assert.Equal(10,skill.ManaCost);Assert.Equal(8,skill.CooldownSeconds);Assert.Equal(2,skill.GroundVolume!.Height);
        Assert.Equal(1,catalog.EchoRecovery.Quantity);
        var seal=catalog.Items[catalog.EchoRecovery.SealItemId];Assert.Equal(ItemUseKind.EchoRestoration,seal.UseKind);Assert.Equal(EquipmentSlot.None,seal.Slot);
        Assert.DoesNotContain(catalog.EchoRecovery.SealItemId,catalog.Creatures["test_adventurer"].StarterItemIds);
        Assert.Null(echo.Stars);Assert.Null(echo.RefusalDuringAction);
    }
    [Theory][InlineData(5,null)][InlineData(4,"FinishCurrent")][InlineData(0,null)][InlineData(6,null)][InlineData(5,"Unknown")]
    public void OnlyFiveStarEchoCanAuthorRefusalAndItsCurrentActionReactionMustBeExplicit(int stars,string? reaction)
    {
        var root=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;root["echoes"]![0]!["stars"]=stars;
        if(reaction is not null)root["echoes"]![0]!["refusalDuringAction"]=reaction;
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }
    [Theory][InlineData("missing")][InlineData("quantity")][InlineData("reference")][InlineData("slot")][InlineData("modifiers")][InlineData("use")]
    public void RecoveryProfileCannotReferenceEquipmentOrInvalidConsumables(string defect)
    {
        var root=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var seal=root["items"]!.AsArray().Single(i=>i!["id"]!.GetValue<string>()=="echo_restoration_seal")!;
        switch(defect)
        {
            case "missing":root.AsObject().Remove("echoRecovery");break;
            case "quantity":root["echoRecovery"]!["quantity"]=0;break;
            case "reference":root["echoRecovery"]!["sealItemId"]="test_sword_item";break;
            case "slot":seal["slot"]="Armor";break;
            case "modifiers":seal["modifiers"]=JsonNode.Parse("{\"maxHealth\":1}");break;
            default:seal["useKind"]="None";break;
        }
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Theory]
    [InlineData("profile")][InlineData("signature")][InlineData("automatic")][InlineData("distance")][InlineData("arrival")][InlineData("route")]
    public void InvalidEchoReferencesAndFollowPoliciesFailAtContentLoad(string defect)
    {
        var root=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var echo=root["echoes"]![0]!;
        switch(defect)
        {
            case "profile":echo["profileId"]="missing";break;
            case "signature":echo["signatureId"]="test_ground_area";break;
            case "automatic":root["abilities"]!.AsArray().Single(a=>a!["id"]!.GetValue<string>()=="mira_signature")!["manualOnly"]=false;break;
            case "distance":echo["teleportDistance"]=1;break;
            case "arrival":echo["arrivalRadius"]=30;break;
            default:echo["unreachableSeconds"]=0;break;
        }
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Fact]
    public void AiCannotUseSignatureAndManualOwnerCommandUsesCommonResourceAndAreaRunner()
    {
        var catalog=ContentCatalogTests.Load();var grid=new NavigationOptions().CreateGrid(new MovementOptions().ToSettings());var spatial=new SpatialIndex(8);
        var combat=new CombatSimulation(catalog,spatial,grid,new CombatOptions(),20,8,()=>1,()=>0);
        var abilities=new AbilitySimulation(catalog,combat,spatial,grid,new CombatOptions(),8,(_,_,_)=>false);
        var owner=new NetworkEntityId(1);var echo=new NetworkEntityId(2);var target=new NetworkEntityId(3);
        combat.Add(owner,new(-7,0),CombatEntityKind.Player);combat.Add(echo,new(-6,0),CombatEntityKind.Echo,"mira_combat");
        combat.Add(target,new(-4,0),CombatEntityKind.Monster);spatial.Add(target,new(-4,0));
        combat.BindCompanion(echo,owner,Guid.NewGuid());abilities.AddActor(echo,catalog.Creatures["mira_combat"]);
        var command=new AbilityCommand(1,0,40,new(-4,0));var ownerMana=combat.Get(owner).Resources.Mana;
        Assert.False(abilities.QueueAiAbility(echo,command,0));
        Assert.False(abilities.QueueCompanionSignature(target,echo,command,0));
        Assert.True(abilities.QueueCompanionSignature(owner,echo,command,0));combat.Simulate(.05f,1);
        var hit=Assert.Single(abilities.Hits);Assert.Equal(echo,hit.ActorId);Assert.Equal(target,hit.TargetId);Assert.Equal(34,hit.Damage,8);
        Assert.Equal(130,abilities.Mana(echo));Assert.Equal(ownerMana,combat.Get(owner).Resources.Mana);
        Assert.Equal(8,Assert.Single(abilities.CaptureCooldowns(echo)).Seconds,8);
        Assert.True(combat.PayHealth(owner,combat.Get(owner).Health,true));
        Assert.False(abilities.QueueCompanionSignature(owner,echo,command with {Sequence=2},2));
    }
}
