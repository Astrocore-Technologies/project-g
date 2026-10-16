using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server.Combat;

public sealed class RespawnResourceTests
{
    [Fact]public void RespawnRestoresApprovedFractionsWithoutClearingCooldownsAcrOrPersistentEffects()
    {
        var json=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        json["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"afterlife","durationSeconds":120,"persistThroughDeath":true,"modifiers":{"maxHealth":20}}"""));
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,hitRoll:()=>0,criticalRoll:()=>1);
        var player=world.AddPlayer(42,new(1));var combat=world.Combat!;var abilities=world.Abilities!;var id=player.EntityId;
        Assert.True(combat.ApplyEffect(default,id,"afterlife"));
        Assert.True(combat.ApplyStatusControl(default,id,1,1,CrowdControlKind.Stun,1));
        var used=combat.Get(id).Controls.Ledger.Snapshot(ControlCategory.Hard,combat.Time).Used;
        combat.Get(id).ReadyAt=combat.Time+30;
        var state=world.CaptureCharacter(42);abilities.Restore(id,state with {Mana=1,Cooldowns=state.Cooldowns.Select(c=>c with {Seconds=30,AvailableCharges=0,RechargeDuration=30}).ToArray()},0);
        Assert.True(combat.PayHealth(id,combat.Get(id).Health,allowFatal:true));
        var cooldowns=abilities.CaptureCooldowns(id);
        combat.Respawn(id);abilities.Respawn(id);
        Assert.Equal(combat.Get(id).Stats.MaxHealth*.5,combat.Get(id).Health);
        Assert.Equal(abilities.Loadout(id,world.Tick).MaxMana*.5,abilities.Mana(id));
        Assert.Equal(catalog.Defense.MaxStamina,combat.DefenseState(id,world.Tick).Stamina);
        Assert.Equal(cooldowns,abilities.CaptureCooldowns(id));Assert.Equal(30,combat.Get(id).ReadyAt-combat.Time);
        Assert.Equal(used,combat.Get(id).Controls.Ledger.Snapshot(ControlCategory.Hard,combat.Time).Used);
        Assert.Single(combat.Get(id).Effects);Assert.Empty(combat.Get(id).Controls.Active);
        world.Simulate(1);Assert.True(combat.Get(id).Health>combat.Get(id).Stats.MaxHealth*.5);
    }
}
