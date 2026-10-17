using Content.Database;

namespace Content.Server.Persistence;

/// <summary>Domain validation stays on the server; Database stores bounded documents.</summary>
public sealed class DatabaseCharacterStore(ICharacterDatabase database, RiverLandingMigration? migration = null) : IRegionalCharacterStore,ICombatCheckpointStore
{
    public async Task<WorldNodeSession> OpenWorldAsync(string key,SavedWorldNode initial,CancellationToken token)
    {
        var lease=await database.OpenWorldAsync(key,initial.Serialize(),token);
        try
        {
            var state=SavedWorldNode.Deserialize(lease.State);
            var ids=(state.Market?.Listings.Select(l=>l.Item.InstanceId)??[]).Concat(state.DeathLoot?.Select(l=>l.Item.InstanceId)??[]).ToArray();
            if(ids.Length>0)
            {
                var wear=await database.LoadItemWearAsync(ids,token);
                SavedItem Update(SavedItem item)=>ItemWearDocuments.Settle(ItemWearDocuments.Apply(item,wear.GetValueOrDefault(item.InstanceId)));
                state=state with
                {
                    Market=state.Market is {} market?market with {Listings=market.Listings.Select(l=>l with {Item=Update(l.Item)}).ToArray()}:null,
                    DeathLoot=state.DeathLoot?.Select(l=>l with {Item=Update(l.Item)}).ToArray()
                };
            }
            return new WorldSession(lease,state);
        }
        catch { await lease.DisposeAsync(); throw; }
    }
    private sealed class WorldSession(DatabaseWorldSession lease,SavedWorldNode state) : WorldNodeSession(lease.Revision,state)
    {
        internal DatabaseWorldSession Lease { get; }=lease;
        internal IReadOnlyDictionary<Guid,long> KnownWear {get;set;}=WorldWear(state);
        public override ValueTask DisposeAsync() => Lease.DisposeAsync();
    }
    public async Task<SocialSession> OpenSocialAsync(CancellationToken token)
    {
        var lease = await database.OpenWorldAsync("social_prototype", "{}", token);
        try { return new SocialLease(lease, await database.LoadSocialAsync("social_prototype", token)); }
        catch { await lease.DisposeAsync(); throw; }
    }
    private sealed class SocialLease(DatabaseWorldSession lease, IReadOnlyList<DatabaseSocialRow> rows) : SocialSession(lease.Revision, rows)
    {
        internal DatabaseWorldSession Lease { get; } = lease;
        public override ValueTask DisposeAsync() => Lease.DisposeAsync();
    }
    public Task SaveSocialCheckpointAsync(IReadOnlyList<CharacterSave> changes, WorldNodeSave? world, SocialSave social, CancellationToken token) => SaveRegionalCheckpointAsync(changes, world is null ? [] : [world], social, token);
    public Task InitializeAsync(CancellationToken token) => database.InitializeAsync(token);
    public async Task<IReadOnlyList<SavedGroundItem>> LoadGroundItemsAsync(IReadOnlyList<SavedGroundItem> seeds, CancellationToken token)
    {
        var rows = await database.LoadGroundItemsAsync(seeds.Select(item => new DatabaseGroundItem(
            item.InstanceId, item.SeedId, item.DefinitionId, item.X, item.Z)).ToArray(), token);
        return rows.Select(item => new SavedGroundItem(item.InstanceId, item.SeedId, item.DefinitionId, item.X, item.Z)).ToArray();
    }
    public async Task<CharacterSession> OpenAsync(string credential, CharacterState initial, CancellationToken token)
    {
        DatabaseSession lease;
        try { lease = await database.OpenAsync(credential, initial.Serialize(), (initial.Inventory ?? SavedInventory.Empty).Serialize(),
            (initial.Echoes ?? SavedEchoes.Empty).Serialize(), token, initial.Progression?.Serialize(),initial.Inventory?.Items.Select(i=>i.InstanceId).ToArray() ?? []); }
        catch (DatabaseInvalidIdentityException) { throw new InvalidIdentityException(); }
        catch (DatabaseCharacterInUseException) { throw new CharacterInUseException(); }
        try
        {
            if (CombatModelMigration.RequiresReset(lease.State))
            {
                // D002: old combat builds may be wiped. Commit character + inventory + Echo + progression
                // together under the existing lease, before publishing any state. Ownership is synced in SaveAsync.
                await database.SaveAsync([new(lease, lease.Revision, initial.Serialize(),
                    (initial.Inventory ?? SavedInventory.Empty).Serialize(), null,
                    (initial.Echoes ?? SavedEchoes.Empty).Serialize(), initial.Progression?.Serialize(),
                    initial.Inventory?.Items.Select(item => item.InstanceId).ToArray() ?? [], [],[],[])], token);
                return new Session(lease, initial) { Revision = checked(lease.Revision + 1) };
            }
            var state = CharacterState.Deserialize(lease.State) with { Inventory = SavedInventory.Deserialize(lease.Inventory), Echoes = SavedEchoes.Deserialize(lease.Echoes), OwnedEchoes=EchoCombatDocuments.Deserialize(lease.OwnedEchoes), Progression = lease.Progression is null ? null : SavedProgression.Deserialize(lease.Progression), Effects = CombatEffectDocuments.Deserialize(lease.Effects),DeathLogs=DeathLogDocuments.Deserialize(lease.DeathLogs) };
            EchoCombatDocuments.ValidateEffectTargets(state.OwnedEchoes!,state.Effects!);
            await database.EnsureInventoryOwnershipAsync(lease,state.Inventory.Items.Select(i=>i.InstanceId).ToArray(),token);
            var wear=await database.LoadItemWearAsync(state.Inventory.Items.Select(i=>i.InstanceId).ToArray(),token);
            state=state with {Inventory=state.Inventory with {Items=state.Inventory.Items.Select(item=>
                ItemWearDocuments.Apply(item,wear.GetValueOrDefault(item.InstanceId))).ToArray()}};
            var migrated = migration?.Apply(state) ?? state;
            var session = new Session(lease, migrated);
            if (!ReferenceEquals(migrated, state))
            {
                // Commit under the exclusive character lease before making the actor visible.
                await database.SaveAsync([new(lease, lease.Revision, migrated.Serialize(), null, null,
                    migrated.Echoes?.Serialize(), migrated.Progression?.Serialize())], token);
                session.Revision++;
            }
            return session;
        }
        catch { await lease.DisposeAsync(); throw; }
    }
    public Task SaveAsync(IReadOnlyList<CharacterSave> changes, CancellationToken token) => SaveWithWorldAsync(changes,null,token);
    public Task SaveWithWorldAsync(IReadOnlyList<CharacterSave> changes,WorldNodeSave? world,CancellationToken token)
    => SaveRegionalCheckpointAsync(changes,world is null ? [] : [world],null,token);
    public Task SaveRegionalCheckpointAsync(IReadOnlyList<CharacterSave> changes,IReadOnlyList<WorldNodeSave> worlds,SocialSave? social,CancellationToken token)
        =>SaveCombatCheckpointAsync(changes,worlds,social,[],token);
    public async Task SaveCombatCheckpointAsync(IReadOnlyList<CharacterSave> changes,IReadOnlyList<WorldNodeSave> worlds,
        SocialSave? social,IReadOnlyList<ItemWearChange> wear,CancellationToken token)
    {
        if (changes.Count > 64 || worlds.Count > Content.Server.Regions.RegionalWorlds.MaxRegions) throw new InvalidDataException("Regional checkpoint exceeds the prototype budget.");
        var writes = new DatabaseSave[changes.Count];
        List<(Session Session,ulong Ordinal)>? journalUpdates=null;
        List<(Session Session,IReadOnlyList<DatabaseEchoRow> Rows)>? echoUpdates=null;
        for (var i = 0; i < writes.Length; i++)
        {
            var change = changes[i];
            if (change.Session is not Session session) throw new ArgumentException("Session belongs to another store.");
            if (change.GroundClaims is { Count: > 0 } claims)
                foreach (var instance in claims)
                    if (change.State.Inventory is null || !change.State.Inventory.Items.Any(item => item.InstanceId == instance && item.EquippedSlot == Content.Shared.Network.EquipmentSlot.None))
                        throw new InvalidDataException("Picked instance missing from inventory.");
            IReadOnlyList<DatabaseDeathLogRow>? journalRows=null;
            DatabaseEchoChanges? echoChanges=null;
            if(change.State.OwnedEchoes is {} roster)
            {
                var serialized=EchoCombatDocuments.Serialize(roster);
                var previous=session.SavedEchoRows.ToDictionary(row=>row.InstanceId);var upserts=new List<DatabaseEchoRow>();
                foreach(var row in serialized)
                    if(!previous.Remove(row.InstanceId,out var old)||old!=row)upserts.Add(row);
                if(upserts.Count>0||previous.Count>0)
                {echoChanges=new(upserts,previous.Keys.ToArray());(echoUpdates??=[]).Add((session,serialized));}
            }
            if(change.State.OwnedEchoes is {} owned && change.State.Effects is {} allEffects)
                EchoCombatDocuments.ValidateEffectTargets(owned,allEffects);
            if(change.State.DeathLogs is {} deaths)
            {
                var ordinal=deaths.Count==0?0:deaths[^1].DeathId;
                if(ordinal<session.DeathLogOrdinal)throw new InvalidDataException("Committed death evidence cannot be rolled back.");
                if(ordinal!=session.DeathLogOrdinal)
                {journalRows=DeathLogDocuments.Serialize(deaths);(journalUpdates??=[]).Add((session,ordinal));}
            }
            writes[i] = new(session.Lease, session.Revision, change.State.Serialize(), change.State.Inventory?.Serialize(), change.GroundClaims, change.State.Echoes?.Serialize(), change.State.Progression?.Serialize(),change.State.Inventory?.Items.Select(i=>i.InstanceId).ToArray(),
                change.State.Effects is {} effects ? CombatEffectDocuments.Serialize(effects) : null,
                journalRows,EchoChanges:echoChanges);
        }
        var worldWrites = new List<DatabaseWorldSave>(worlds.Count);
        foreach (var world in worlds)
        {
            if (world.Session is not WorldSession owner) throw new ArgumentException("Foreign world session.");
            worldWrites.Add(new(owner.Lease,owner.Revision,world.State.Serialize(),world.Audit,(world.State.Market?.Listings.Select(l=>l.Item.InstanceId) ?? []).Concat(world.State.DeathLoot?.Select(l=>l.Item.InstanceId) ?? []).ToArray()));
        }
        DatabaseSocialSave? socialWrite = null;
        if (social is not null)
        {
            if (social.Session is not SocialLease owner) throw new ArgumentException("Foreign social session.");
            socialWrite = new(new(owner.Lease, owner.Revision, "{}", [new("social", "SocialCheckpoint", "Rows " + social.Rows.Count, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())], []), social.Rows);
        }
        var completeWear=await CompleteWearWrites(changes,worlds,wear,token);
        if(completeWear.Count==0)await database.SaveRegionalCheckpointAsync(writes,worldWrites,socialWrite,token);
        else await database.SaveCombatCheckpointAsync(writes,worldWrites,socialWrite,
            completeWear.Select(w=>new DatabaseItemWear(w.ItemId,w.ExpectedTotal,w.Total)).ToArray(),token);
        // Ordinary movement/resource checkpoints do not serialize or rewrite 3000 historic entries.
        // Advance this cache only after the whole fenced transaction succeeds.
        if(journalUpdates is not null)foreach(var update in journalUpdates)update.Session.DeathLogOrdinal=update.Ordinal;
        if(echoUpdates is not null)foreach(var update in echoUpdates)update.Session.SavedEchoRows=update.Rows;
        foreach(var change in changes)if(change.State.Inventory is {} items)((Session)change.Session).KnownWear=InventoryWear(items);
        foreach(var world in worlds)((WorldSession)world.Session).KnownWear=WorldWear(world.State);
    }
    private async Task<IReadOnlyList<ItemWearChange>> CompleteWearWrites(IReadOnlyList<CharacterSave> changes,
        IReadOnlyList<WorldNodeSave> worlds,IReadOnlyList<ItemWearChange> explicitWrites,CancellationToken token)
    {
        var included=explicitWrites.Select(w=>w.ItemId).ToHashSet();var candidates=new Dictionary<Guid,long>();
        void Consider(Guid id,long total,IReadOnlyDictionary<Guid,long> known)
        {if(total>known.GetValueOrDefault(id)&&!included.Contains(id))candidates[id]=Math.Max(candidates.GetValueOrDefault(id),total);}
        foreach(var change in changes)
            if(change.State.Inventory is {} inventory)
                foreach(var item in inventory.Items)Consider(item.InstanceId,item.Condition?.AppliedWeaponWear??0,((Session)change.Session).KnownWear);
        foreach(var world in worlds)
            foreach(var (id,total) in WorldWear(world.State))Consider(id,total,((WorldSession)world.Session).KnownWear);
        if(candidates.Count==0)return explicitWrites;
        var result=new List<ItemWearChange>(explicitWrites);
        foreach(var batch in candidates.Keys.Chunk(64))
        {
            var stored=await database.LoadItemWearAsync(batch,token);
            foreach(var id in batch)
                if(candidates[id]>stored.GetValueOrDefault(id))result.Add(new(id,stored.GetValueOrDefault(id),candidates[id]));
        }
        return result;
    }
    private static IReadOnlyDictionary<Guid,long> InventoryWear(SavedInventory inventory)=>inventory.Items
        .ToDictionary(i=>i.InstanceId,i=>i.Condition?.AppliedWeaponWear??0);
    private static IReadOnlyDictionary<Guid,long> WorldWear(SavedWorldNode state)=>(state.Market?.Listings.Select(l=>l.Item)??[])
        .Concat(state.DeathLoot?.Select(l=>l.Item)??[]).ToDictionary(i=>i.InstanceId,i=>i.Condition?.AppliedWeaponWear??0);
    private sealed class Session(DatabaseSession lease, CharacterState state)
        : CharacterSession(lease.CharacterId, lease.OwnerId, lease.Revision, state, lease.IssuedToken)
    {
        internal DatabaseSession Lease { get; } = lease;
        internal IReadOnlyDictionary<Guid,long> KnownWear {get;set;}=state.Inventory is {} inventory?InventoryWear(inventory):new Dictionary<Guid,long>();
        internal ulong DeathLogOrdinal {get;set;}=state.DeathLogs is {Count:>0} deaths?deaths[^1].DeathId:0;
        internal IReadOnlyList<DatabaseEchoRow> SavedEchoRows {get;set;}=state.OwnedEchoes is {} roster ? EchoCombatDocuments.Serialize(roster) : [];
        public override ValueTask DisposeAsync() => Lease.DisposeAsync();
    }
}
