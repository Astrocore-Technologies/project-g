using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Server.Regions;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server;

public sealed class RegionalTravelTests
{
    [Fact]
    public async Task CombatTagBlocksTravelUntilItExpires()
    {
        await using var f = await Fixture.Create(); await f.Add();
        Assert.True(f.Source.Combat!.ApplyAbilityDamage(f.Source.TrainingTargetId,1,f.Source.GetPlayer(42).EntityId)>0);
        f.Realm.Simulate(.05f);
        Assert.True(f.Source.IsCombatTagged(42)); Assert.False(f.Begin()); Assert.False(f.Realm.HasPendingJourney);
        f.Now += 30_001; f.Realm.Simulate(.05f);
        Assert.False(f.Source.IsCombatTagged(42)); Assert.True(f.Begin()); await f.Complete();
    }

    [Fact]
    public async Task OwnedCombatCargoPreservesResourcesAndRelocatesOnlyTheActiveEcho()
    {
        await using var f=await Fixture.Create();
        var owned=new SavedEchoCombat
        {
            InstanceId=Guid.NewGuid(),DefinitionId="test_guardian_echo",ProfileId="mira_combat",
            Availability=SavedEchoAvailability.Ready,Stats=new(10,10,10,10,10,10,10),
            X=13.5f,Z=10,Health=51,Mana=27,Defense=new(1,31,2,1),CombatControl=new(1,0,0,0,0,0),
            AttackCooldownSeconds=2,Cooldowns=[new("mira_signature",4)],SavedAtUnixMilliseconds=f.Now
        };
        var ready=owned with {InstanceId=Guid.NewGuid(),Availability=SavedEchoAvailability.Ready,ActiveSlot=null,X=2,Z=2};
        var initial=PvpTests.Initial(f.Source,13,10) with {OwnedEchoes=[owned,ready]};
        var first=await f.Store.OpenAsync("",initial,default);var token=first.IssuedToken;
        await f.Store.SaveAsync([new(first,initial)],default);await first.DisposeAsync();
        var session=await f.Store.OpenAsync(token,initial,default);f.Sessions.Add(session);
        f.Realm.AddPlayer(42,new(42),session);EchoTests.Summon(f.Source,42,owned.InstanceId);
        var body=f.Source.Echoes!.Loadout(f.Source.GetPlayer(42).EntityId,f.Source.Tick).Slots[0].EntityId;
        var before=f.Source.CaptureCharacter(42).OwnedEchoes!.Single(e=>e.InstanceId==owned.InstanceId);
        Content.Server.Combat.CompanionRuntimeState.Restore(f.Source.Combat!,f.Source.Abilities!,body,before with
        {Health=51,Mana=27,Defense=owned.Defense,Cooldowns=owned.Cooldowns},[],0,false);
        before=f.Source.CaptureCharacter(42).OwnedEchoes!.Single(e=>e.InstanceId==owned.InstanceId);
        var readyBefore=f.Source.CaptureCharacter(42).OwnedEchoes!.Single(e=>e.InstanceId==ready.InstanceId);
        Assert.True(f.Begin());await f.Complete();
        var restored=f.Target.CaptureCharacter(42).OwnedEchoes!;
        var active=Assert.Single(restored,e=>e.Availability==SavedEchoAvailability.Active);
        Assert.Equal(owned.InstanceId,active.InstanceId);Assert.Equal((byte)1,active.ActiveSlot);
        Assert.Equal(before.X-25,active.X);Assert.Equal(before.Z,active.Z);Assert.Equal(51,active.Health);Assert.Equal(27,active.Mana);
        Assert.Equal(31,active.Defense.Stamina);Assert.Equal(4,active.Cooldowns[0].Seconds);Assert.Equal(owned.SavedAtUnixMilliseconds,active.SavedAtUnixMilliseconds);
        Assert.Equal(EchoCombatDocuments.Serialize([readyBefore]).Single().State,
            EchoCombatDocuments.Serialize([Assert.Single(restored,e=>e.Availability==SavedEchoAvailability.Ready)]).Single().State);
    }
    internal sealed class Fixture : IAsyncDisposable
    {
        public SqliteCharacterStore Store { get; } = new();
        public Dictionary<string, WorldNodeSession> Leases { get; } = new();
        public List<CharacterSession> Sessions { get; } = [];
        public ServerWorld Source { get; private set; } = null!;
        public ServerWorld Target { get; private set; } = null!;
        public RegionalSimulation Realm { get; private set; } = null!;
        public SocialSession SocialLease { get; private set; } = null!;
        public long Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); await f.Store.InitializeAsync(default);
            var catalog = ContentCatalogTests.Load(); var ids = new RuntimeEntityAllocator();
            ServerWorld CreateWorld(string region) => new(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
                Options.Create(new NavigationOptions { BlockedAreas = [new() { X = 14, Z = 10, Width = 2, Height = 10 }] }), catalog,
                inventory: Options.Create(new InventoryOptions { Enabled = true }),
                groundItems: Options.Create(new GroundItemOptions { Enabled = true, Seeds = [] }),
                // The world-node fixture needs an NPC; keep it away from the travel/summon scenario.
                npc: Options.Create(new NpcOptions { Enabled = true,X=-13,Z=-13 }),
                worldStory: Options.Create(new WorldStoryOptions { Enabled = true }),
                starterZone: Options.Create(new StarterZoneOptions { Enabled = true }),
                crafting: Options.Create(new CraftingOptions { Enabled = true }),
                echoes: Options.Create(new EchoOptions { Enabled = true }), regionId: region, entityIds: ids,
                worldNodeDefinition: catalog.WorldNode! with { Key = region + "_crossing" }, hitRoll: () => 0, criticalRoll: () => 1);
            f.Source = CreateWorld("prototype"); f.Target = CreateWorld("outskirts");
            foreach (var world in new[] { f.Source, f.Target })
            {
                var lease = await f.Store.OpenWorldAsync(world.WorldNodeKey, new(), default);
                f.Leases.Add(world.WorldNodeKey, lease); world.RestoreWorldNode(lease.State, lease.Revision);
            }
            f.SocialLease = await f.Store.OpenSocialAsync(default);
            f.Realm = new([f.Source, f.Target], [
                new("prototype", "outskirts", new(13, 10), new(-12, 10), 1),
                new("outskirts", "prototype", new(-14, 10), new(11, 10), 1)
            ], f.SocialLease.Rows, () => f.Now);
            return f;
        }

        public async Task<CharacterSession> Add(int connection = 42, float x = 13, float z = 10, byte discoveries = 0)
        {
            var state = PvpTests.Initial(Source, x, z) with
            {
                Mana = 140, AttackCooldownSeconds = 4,
                Echoes = new() { Active = Enumerable.Range(1, 3).Select(i => new SavedEcho(
                    Guid.NewGuid(), "test_guardian_echo", (byte)i, x, z + i * .2f, 2, 6)).ToArray() }
            };
            state = state with { Progression = state.Progression! with { Discoveries = discoveries } };
            var session = await Store.OpenAsync("", state, default); Sessions.Add(session);
            Realm.AddPlayer(connection, new((ulong)connection), session);
            uint sequence=0;foreach(var echo in state.Echoes!.Active)EchoTests.Summon(Source,connection,echo.InstanceId,++sequence);
            var actor=Source.Combat!.Get(Source.GetPlayer(connection).EntityId);actor.Resources.Mana=3;actor.ReadyAt=Source.Combat.Time+4;
            return session;
        }

        public bool Begin(int connection = 42) => Realm.TryBeginTravel(connection, Store, Leases, SocialLease);
        public async Task<RegionArrival> Complete()
        {
            for (var i = 0; i < 500; i++)
            {
                if (Realm.TryCompleteTravel(out var arrival)) return arrival!;
                await Task.Delay(10);
            }
            throw new TimeoutException("Regional checkpoint did not complete.");
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var session in Sessions) await session.DisposeAsync();
            await SocialLease.DisposeAsync(); foreach (var lease in Leases.Values) await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task CommitGateFreezesBothCopiesPreservesThreeEchoesAndKeepsTheLease()
    {
        await using var f = await Fixture.Create(); var session = await f.Add();
        var oldOwner = f.Realm.Owner(42); var oldId = f.Source.GetPlayer(42).EntityId;
        var before = f.Source.CaptureCharacter(42); var revision = session.Revision;
        var gate = f.Store.PauseSaves(); Assert.True(f.Begin());
        Assert.True(f.Realm.HasPendingJourney); Assert.True(f.Store.IsHeld);
        Assert.Equal("prototype", f.Store.SingleState.RegionId);
        Assert.Empty(f.Source.Players); Assert.Empty(f.Target.Players);
        Assert.True(f.Realm.Social.IsOnline(session.CharacterId));
        Assert.False(f.Realm.CanExecute(42, oldOwner));
        Assert.False(f.Source.TryApplyMove(42, new(1, 0, new(13, 11))));
        Assert.Throws<InvalidOperationException>(() => f.Realm.Simulate(.05f));
        Assert.False(f.Realm.TryCompleteTravel(out _));
        Assert.Throws<InvalidOperationException>(() => f.Realm.RemovePlayer(42));
        gate.SetResult(); var arrival = await f.Complete();
        Assert.Equal("outskirts", arrival.Owner.Region); Assert.Equal(oldId, arrival.PreviousEntity);
        Assert.NotEqual(oldId, arrival.Player.EntityId); Assert.False(f.Source.IsPlayer(oldId));
        Assert.Equal(revision + 1, session.Revision); Assert.True(f.Store.IsHeld);
        Assert.Equal("outskirts", f.Store.SingleState.RegionId);
        Assert.False(f.Realm.CanExecute(42, oldOwner)); Assert.True(f.Realm.CanExecute(42, arrival.Owner));
        var after = f.Target.CaptureCharacter(42);
        Assert.Equal(before.Health, after.Health); Assert.Equal(before.Mana, after.Mana);
        Assert.Equal(before.Inventory!.Serialize(), after.Inventory!.Serialize());
        Assert.Equal(before.OwnedEchoes!.Select(e=>e.InstanceId),after.OwnedEchoes!.Select(e=>e.InstanceId));
        Assert.Equal(before.OwnedEchoes!.Select(e=>e.ActiveSlot),after.OwnedEchoes!.Select(e=>e.ActiveSlot));
        Assert.Equal(3,after.OwnedEchoes!.Count(e=>e.Availability==SavedEchoAvailability.Active));
        Assert.Equal(before.Progression!.Profession, after.Progression!.Profession);
        using var c = WorldNodeStorageTests.Connect(f.Store.DatabasePath); using var q = c.CreateCommand();
        q.CommandText = "SELECT count(*) FROM world_audit WHERE operation IN ('RegionArrival','RegionDeparture')";
        Assert.Equal(2L, q.ExecuteScalar());
    }

    [Fact]
    public async Task TravelIsNotLogoutAndRemotePartyPresenceDoesNotRevealCoordinates()
    {
        await using var f = await Fixture.Create(); var a = await f.Add(); var b = await f.Add(43, -9, 0);
        var social = f.Realm.Social;
        Assert.Same(f.Source.Social, f.Target.Social);
        Assert.Equal(SocialOutcome.Accepted, SocialTests.Command(social, a.CharacterId, SocialAction.Create));
        Assert.Equal(SocialOutcome.Accepted, SocialTests.Command(social, a.CharacterId, SocialAction.Invite, target: b.CharacterId));
        var invite = Assert.Single(social.Invitations(b.CharacterId));
        Assert.True(f.Begin()); Assert.Single(social.Invitations(b.CharacterId)); await f.Complete();
        Assert.Single(social.Invitations(b.CharacterId));
        Assert.Equal(SocialOutcome.Accepted, SocialTests.Command(social, b.CharacterId, SocialAction.Accept, invite: invite));
        Assert.Equal(a.CharacterId, social.Group(b.CharacterId, SocialKind.Party)!.Leader);
        Assert.True(social.IsOnline(a.CharacterId)); Assert.True(social.IsOnline(b.CharacterId));
        var presence = Assert.Single(f.Source.PartyPresence(43));
        var remote = Assert.Single(presence.Members, m => m.Handle == social.Identity(a.CharacterId).Handle);
        Assert.Equal(0, remote.Flags); Assert.Equal(0, remote.Health); Assert.Equal(Vector2.Zero, remote.Position);
        Assert.All(f.Source.SocialRosters(43).Single(r => r.Kind == SocialKind.Party).Members, m => Assert.True(m.Online));
    }

    [Fact]
    public async Task SharedAuthorityChecksCombatEligibilityInTheMembersActualRegion()
    {
        await using var f = await Fixture.Create(); var a = await f.Add(); var b = await f.Add(43, -9, 0);
        var social = f.Realm.Social;
        SocialTests.Command(social, a.CharacterId, SocialAction.Create);
        SocialTests.Command(social, a.CharacterId, SocialAction.Invite, target: b.CharacterId);
        SocialTests.Command(social, b.CharacterId, SocialAction.Accept, invite: Assert.Single(social.Invitations(b.CharacterId)));
        Assert.True(f.Begin()); await f.Complete();
        Assert.True(f.Target.TryQueuePvp(42, new(1, PvpAction.Mode, PvpMode.Voluntary, false, 0)));
        f.Realm.Simulate(.05f);
        Assert.Equal(SocialOutcome.Busy, SocialTests.Command(social, a.CharacterId, SocialAction.Kick, target: b.CharacterId));
    }

    [Fact]
    public async Task BothRegionalMapsSurviveARoundTripWithoutRevealingTheDestination()
    {
        await using var f = await Fixture.Create(); await f.Add(discoveries: 3); f.Realm.Simulate(.05f);
        var original = f.Source.CaptureCharacter(42).Progression!.Exploration!;
        Assert.True(f.Begin()); await f.Complete();
        var first = f.Target.CaptureCharacter(42).Progression!;
        Assert.Null(first.Exploration); Assert.Equal(original.Cells, Assert.Single(first.OtherExplorations!).Cells);
        f.Realm.Simulate(.05f);
        var targetMap = f.Target.CaptureCharacter(42).Progression!.Exploration!;
        Assert.Equal("outskirts", targetMap.RegionKey); Assert.Equal((byte)0, targetMap.Places);
        Assert.True(f.Target.TryApplyMove(42, new(1, 0, new(-14, 10))));
        for (var i = 0; i < 20; i++) f.Realm.Simulate(.05f);
        var outer = f.Target.CaptureCharacter(42).Progression!.Exploration!;
        Assert.True(f.Begin()); await f.Complete();
        var returned = f.Source.CaptureCharacter(42).Progression!;
        Assert.Equal(original.Cells, returned.Exploration!.Cells);
        Assert.Equal("prototype", returned.Exploration.RegionKey);
        Assert.Equal(outer.Cells, Assert.Single(returned.OtherExplorations!).Cells);
    }

    [Fact]
    public async Task BoundaryIsServerSelectedAndCannotBeRequestedFromFarAway()
    {
        await using var f = await Fixture.Create(); await f.Add(x: -9, z: 0);
        Assert.False(f.Begin()); Assert.False(f.Realm.HasPendingJourney);
        Assert.Single(f.Source.Players); Assert.Empty(f.Target.Players);
    }

    [Fact]
    public async Task StaleWorldFenceRollsBackAllRegionsCharacterAndSocialThenStopsTheRealm()
    {
        await using var f = await Fixture.Create(); var session = await f.Add();
        var targetLease = f.Leases[f.Target.WorldNodeKey];
        await f.Store.SaveWithWorldAsync([], new(targetLease, new() { StormRumor = true },
            [new("test", "Fence", "external revision", f.Now)]), default);
        Assert.True(f.Begin());
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Complete());
        Assert.True(f.Realm.Faulted); Assert.Equal("prototype", f.Store.SingleState.RegionId);
        Assert.False(f.Realm.CanExecute(42, f.Realm.Owner(42)));
        Assert.Throws<InvalidOperationException>(() => f.Realm.Simulate(.05f));
        using var c = WorldNodeStorageTests.Connect(f.Store.DatabasePath); using var q = c.CreateCommand();
        q.CommandText = "SELECT count(*) FROM social_records"; Assert.Equal(0L, q.ExecuteScalar());
        q.CommandText = "SELECT count(*) FROM world_audit WHERE operation IN ('RegionArrival','RegionDeparture')";
        Assert.Equal(0L, q.ExecuteScalar());
    }

    [Fact]
    public async Task ReopeningTheSameCredentialLoadsOnlyTheDurableDestination()
    {
        await using var f = await Fixture.Create(); var session = await f.Add(); var credential = session.IssuedToken;
        Assert.True(f.Begin()); await f.Complete();f.Target.FinishDeparture(42);
        await f.Store.SaveAsync([new(session,f.Target.CaptureCharacter(42))],default);
        f.Realm.RemovePlayer(42); await session.DisposeAsync();
        await using var reopened = await f.Store.OpenAsync(credential, f.Source.CreateInitialCharacter(), default);
        Assert.Equal(session.CharacterId, reopened.CharacterId); Assert.Equal("outskirts", reopened.State.RegionId);
        var actor = f.Realm.AddPlayer(42, new(42), reopened);
        Assert.Single(f.Target.Players); Assert.Empty(f.Source.Players);
        Assert.Equal(3, f.Target.CaptureCharacter(42).OwnedEchoes!.Count);
        Assert.All(f.Target.CaptureCharacter(42).OwnedEchoes!,e=>Assert.Equal(SavedEchoAvailability.Ready,e.Availability));
        Assert.Empty(f.Target.Echoes!.Loadout(actor.EntityId,f.Target.Tick).Slots);
        Assert.True(actor.EntityId.IsValid);
    }
}
