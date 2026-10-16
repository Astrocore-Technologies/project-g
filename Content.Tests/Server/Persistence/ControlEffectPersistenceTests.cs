using System.Numerics;
using Content.Database;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Persistence;

public sealed class ControlEffectPersistenceTests
{
    [Fact]
    public async Task DifferentSourcesAndControlKindsSurviveDatabaseRestartWithAcrAndRefreshIdentity()
    {
        var world = World(); var target = world.AddPlayer(42, new(1));
        var source = world.AddPlayer(43, new(2)); var sourceIdentity = Guid.NewGuid();
        world.BindWorldActor(43, sourceIdentity); world.Combat!.DamagePermission = (_, _) => true;
        world.Combat.ApplyStatusControl(source.EntityId, target.EntityId, 7, 1, CrowdControlKind.Root, 3);
        world.Combat.ApplyStatusControl(source.EntityId, target.EntityId, 8, 2, CrowdControlKind.Blind, 4, 20);
        world.Combat.ApplyStatusControl(default, target.EntityId, 8, 3, CrowdControlKind.Blind, 4, 10);
        var blindStrength=world.Combat.Get(target.EntityId).Controls.Strength(CrowdControlKind.Blind);
        var saved = world.CaptureCharacter(42); var expected = saved.Effects!.ToArray();
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None);
        var session = await store.OpenAsync("", world.CreateInitialCharacter(), CancellationToken.None);
        var identity = session.IssuedToken;
        await store.SaveAsync([new(session, saved)], CancellationToken.None); await session.DisposeAsync();
        await using var restored = await store.OpenAsync(identity, world.CreateInitialCharacter(), CancellationToken.None);
        Assert.Equal(3, restored.State.Effects!.Count);
        Assert.Equal(expected.OrderBy(e => e.InstanceId), restored.State.Effects.OrderBy(e => e.InstanceId));
        var nextWorld = World(); nextWorld.AddPlayer(99, new(99)); // Ensure different runtime IDs.
        var restoredTarget = nextWorld.AddPlayer(42, new(1), restored.State with { SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        var restoredSource = nextWorld.AddPlayer(43, new(2)); nextWorld.BindWorldActor(43, sourceIdentity);
        nextWorld.Combat!.DamagePermission = (_, _) => true;
        var actor = nextWorld.Combat.Get(restoredTarget.EntityId);
        Assert.True(actor.Controls.Has(CrowdControlKind.Root)); Assert.Equal(blindStrength, actor.Controls.Strength(CrowdControlKind.Blind));
        Assert.NotEqual(source.EntityId, restoredSource.EntityId);
        Assert.Equal(new CombatSourceId(CombatSourceKind.Character, sourceIdentity),
            actor.Controls.Active.Single(e => e.Key.Kind == CrowdControlKind.Root).Key.Source);
        var used = actor.Controls.Ledger.Snapshot(ControlCategory.Soft, nextWorld.Combat.Time).Used;
        var previousEnd=actor.Controls.Active.Single(e=>e.Key.Kind==CrowdControlKind.Blind && e.Key.Source.Id==sourceIdentity).EndsAt;
        for (var i = 0; i < 30; i++) nextWorld.Simulate(.05f);
        Assert.True(nextWorld.Combat.ApplyStatusControl(restoredSource.EntityId, restoredTarget.EntityId, 8, 100,
            CrowdControlKind.Blind, 4, 999));
        Assert.Equal(3, actor.Controls.Active.Count()); // Same source refreshes instead of creating a new buff.
        Assert.Equal(blindStrength, actor.Controls.Strength(CrowdControlKind.Blind));
        var extension=actor.Controls.Active.Single(e=>e.Key.Kind==CrowdControlKind.Blind && e.Key.Source.Id==sourceIdentity).EndsAt-previousEnd;
        Assert.Equal(used + extension * .2, actor.Controls.Ledger.Snapshot(ControlCategory.Soft, nextWorld.Combat.Time).Used, 6);
    }

    [Fact]
    public void OfflineExpiryPreservesRemainingInstancesWithoutRechargingAcr()
    {
        var world = World(); var player = world.AddPlayer(42, new(1)); var combat = world.Combat!;
        combat.ApplyStatusControl(default, player.EntityId, 1, 1, CrowdControlKind.Root, 3);
        combat.ApplyStatusControl(default, player.EntityId, 2, 2, CrowdControlKind.Blind, 5, 10);
        var state = world.CaptureCharacter(42); var next = World(); var restored = next.AddPlayer(42, new(1));
        next.Combat!.RestoreControl(restored.EntityId, state.CombatControl, 4);
        next.Combat.RestoreEffects(restored.EntityId, state.Effects, 4);
        var actor = next.Combat.Get(restored.EntityId);
        Assert.False(actor.Controls.Has(CrowdControlKind.Root)); Assert.True(actor.Controls.Has(CrowdControlKind.Blind));
        Assert.Equal(.5, actor.Controls.Ledger.Snapshot(ControlCategory.Soft, 0).Used);
        Assert.Equal(1, actor.Controls.LastEnd(CrowdControlKind.Blind));
        Assert.True(next.Combat.RemoveControl(restored.EntityId, CrowdControlKind.Blind));
        Assert.Equal(.5, actor.Controls.Ledger.Snapshot(ControlCategory.Soft, 0).Used);
    }

    [Fact]
    public void TauntTracksSourceAfterReconnectionWithNewNetworkId()
    {
        var world = World(); var target = world.AddPlayer(42, new(1)); var source = world.AddPlayer(43, new(2));
        var identity = Guid.NewGuid(); world.BindWorldActor(43, identity); world.Combat!.DamagePermission = (_, _) => true;
        world.Combat.ApplyStatusControl(source.EntityId, target.EntityId, 1, 1, CrowdControlKind.Taunt, 2, .3);
        var saved = world.CaptureCharacter(42);
        world.RemovePlayer(43); world.AddPlayer(99, new(99)); var reconnected = world.AddPlayer(43, new(2));
        world.BindWorldActor(43, identity);
        Assert.Equal(reconnected.EntityId, world.Combat.ResolveSource(world.Combat.Get(target.EntityId).Controls.Latest(CrowdControlKind.Taunt)!.Key.Source));
        var normal = world.Combat.ResolveDirectHit(target.EntityId, reconnected.EntityId, Vector2.UnitX, 10, false);
        var penalized = world.Combat.ResolveDirectHit(target.EntityId, world.TrainingTargetId, Vector2.UnitX, 10, false);
        var baseline = world.Combat.ResolveDirectHit(reconnected.EntityId, world.TrainingTargetId, Vector2.UnitX, 10, false);
        Assert.True(normal.Damage > 0); Assert.Equal(baseline.Damage * .7, penalized.Damage, 8);
        Assert.DoesNotContain("NetworkEntityId", CombatEffectDocuments.Serialize(saved.Effects!).Single().State);
    }

    [Fact]
    public void ServerRestartFinishesFlightWithoutConvertingItIntoAnExtraKnockdownOrRefundingAcr()
    {
        var world=World(); var player=world.AddPlayer(42,new(1));
        Assert.True(world.Combat!.Knockup(player.EntityId,1,1,1));
        var state=world.CaptureCharacter(42); Assert.Equal(CombatControlPhase.Airborne,state.CombatControl!.Phase);
        var next=World(); var restored=next.AddPlayer(42,new(1),state);
        var actor=next.Combat!.Get(restored.EntityId);
        Assert.Equal(CombatControlPhase.None,actor.Control); Assert.Equal(0,actor.AirOffset); Assert.Empty(actor.Controls.Active);
        var acr=actor.Controls.Ledger.Snapshot(ControlCategory.Hard,next.Combat.Time);
        Assert.Equal(2,acr.Used); Assert.Equal(12,acr.ResetInSeconds,6);
    }

    [Fact]
    public void ServerRestartPreservesAlreadyGroundedKnockdown()
    {
        var world=World(); var player=world.AddPlayer(42,new(1));
        Assert.True(world.Combat!.Knockup(player.EntityId,.2,1,1)); world.Simulate(.25f);
        var state=world.CaptureCharacter(42); Assert.Equal(CombatControlPhase.KnockedDown,state.CombatControl!.Phase);
        var next=World(); var restored=next.AddPlayer(42,new(1),state);
        Assert.Equal(CombatControlPhase.KnockedDown,next.Combat!.Get(restored.EntityId).Control);
        Assert.Single(next.Combat.Get(restored.EntityId).Controls.Active);
    }

    [Fact]
    public void DocumentsRejectUnknownTypesDuplicateIdsAndRowIdentityMismatch()
    {
        var world = World(); var player = world.AddPlayer(42, new(1));
        world.Combat!.Stun(player.EntityId, 1); var saved = world.CaptureCharacter(42).Effects!;
        var row = CombatEffectDocuments.Serialize(saved).Single();
        Assert.Throws<InvalidDataException>(() => CombatEffectDocuments.Deserialize([row with { InstanceId = Guid.NewGuid() }]));
        Assert.Throws<InvalidDataException>(() => CombatEffectDocuments.Deserialize([row, row]));
        Assert.Throws<InvalidDataException>(() => CombatEffectDocuments.Deserialize([row with { State = row.State.Replace("\"control\"", "\"unknown\"") }]));
    }

    private static ServerWorld World() => new(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
        catalog: ContentCatalogTests.Load(), hitRoll: () => 0, criticalRoll: () => 1);
}
