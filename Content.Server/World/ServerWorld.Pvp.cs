using System.Numerics;
using System.Security.Cryptography;
using Content.Server.Combat;
using Content.Server.Persistence;
using Content.Shared.Network;
namespace Content.Server.World;
public sealed partial class ServerWorld
{
    private readonly Dictionary<NetworkEntityId,SavedPvp> _pvp=new();
    private readonly Dictionary<NetworkEntityId,Guid> _pvpIdentities=new();
    private readonly Dictionary<int,PvpCommand> _pvpPending=new();
    private readonly Dictionary<int,uint> _pvpRequests=new();
    private readonly HashSet<NetworkEntityId> _pvpDirty=new(),_pvpTimed=new(),_lethal=new();
    private readonly Dictionary<NetworkEntityId,PvpResult> _pvpResults=new();
    private static Func<long> StableClock(){var origin=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();var stamp=System.Diagnostics.Stopwatch.GetTimestamp();return ()=>origin+(long)System.Diagnostics.Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;}
    internal Func<long> PvpClock {get;set;}=StableClock();
    internal long PvpNow=>PvpClock();
    internal Func<int,int> DeathRoll {get;set;}=n=>RandomNumberGenerator.GetInt32(n);
    private readonly Content.Server.Pvp.PvpDefinition? _pvpDefinition;
    private Content.Server.Pvp.PvpDefinition Rules=>_pvpDefinition!;
    public bool HasPvp=>HasWorldNode&&Inventory is not null&&GroundItems is not null&&_pvpDefinition is not null;
    public IReadOnlyDictionary<NetworkEntityId,PvpResult> PvpResults=>_pvpResults;
    public bool IsPvpDirty(NetworkEntityId id)=>_pvpDirty.Contains(id);
    public bool IsCombatTagged(int connection)=>HasPvp&&_playersByConnection.TryGetValue(connection,out var p)&&_pvp[p.EntityId].CombatUntil>PvpClock();
    private void InitializePvp()
    {
        if(!HasPvp)return;
        if(!Navigation.IsWalkable(Rules.Respawn))throw new InvalidDataException("Blocked respawn position.");
        Combat!.DamageOriginPermission=position=>!Rules.IsSafe(position); Combat.DamagePermission=PvpCanHit;Combat.DamageApplied=PvpDamage;Combat.PlayerAction=PvpActionStarted;
        Inventory!.ActionStarted=id=>GroundItems!.CancelChannel(id,Tick);
        GroundItems!.Clock=()=>PvpClock();GroundItems.ChannelSeconds=Rules.PickupSeconds;
        GroundItems.CanChannel=id=>_playersByEntity.TryGetValue(id,out var p)&&!p.Motion.IsMoving&&!p.Motion.IsDashing&&!Combat.Get(id).IsCasting&&!Abilities!.HasActiveEffects(id)&&!Inventory!.IsTrading(id)&&_pvp.TryGetValue(id,out var state)&&state.CombatUntil>PvpClock();
        GroundItems.LootRemoved=(item,expired)=>{_nodeState=_nodeState with {DeathLoot=(_nodeState.DeathLoot??[]).Where(l=>l.Item.InstanceId!=item.InstanceId).ToArray()};Audit("world",expired?"DeathLootExpired":"DeathLootPicked",item.InstanceId.ToString("N"));};
    }
    private void AddPvp(NetworkEntityId id,SavedPvp? saved)
    {
        if(!HasPvp)return;var state=saved??new SavedPvp();state.Validate();
        if(saved is not null && saved.Dead!=(Combat!.Get(id).Health<=0))throw new InvalidDataException("Death/HP mismatch.");
        if(saved is null&&Combat!.Get(id).Health<=0)state=state with {Dead=true,DeathId=1,RespawnAt=PvpClock()+Rules.PveRespawnSeconds*1000L};
        _pvp.Add(id,state);_pvpIdentities[id]=Guid.NewGuid();DirtyPvp(id);
    }
    private void DirtyPvp(NetworkEntityId id){_pvpDirty.Add(id);_pvpTimed.Add(id);MarkPersistent(id);}
    private bool PvpCanHit(NetworkEntityId source,NetworkEntityId target)
    {
        if(!Combat!.TryGet(target,out var victim)||victim.Health<=0||source==target)return false;
        if(!Combat.TryGet(source,out var actor))return CombatSimulation.IsHostileTarget(victim.Kind);
        if(actor.Health<=0)return false;
        if(CombatSimulation.IsNpc(actor.Kind))return victim.Kind==CombatEntityKind.Player;
        if(victim.Kind!=CombatEntityKind.Player)return CombatSimulation.IsHostileTarget(victim.Kind);
        if(!_pvp.TryGetValue(source,out var attacker)||!_pvp.TryGetValue(target,out var defender))return false;
        if(Social?.SameParty(_pvpIdentities[source],_pvpIdentities[target])==true)return false;
        var now=PvpClock();
        if(Rules.IsSafe(actor.Position)||Rules.IsSafe(victim.Position)||defender.ProtectionUntil>now)return false;
        if(defender.Mode!=PvpMode.Peaceful||defender.CombatUntil>now||defender.AggressorUntil>now)return true;
        if(attacker.Mode!=PvpMode.Criminal)return false;
        var owner=_pvpIdentities[target];return attacker.Episodes.Any(e=>e.Victim==owner&&e.Until>now)||attacker.Episodes.Count(e=>e.Until>now)<16;
    }
    private void PvpActionStarted(NetworkEntityId id)
    {
        if(!_pvp.TryGetValue(id,out var s))return;GroundItems!.CancelChannel(id,Tick);
        var now=PvpClock();var next=s with {ProtectionUntil=0};
        if(s.Mode==PvpMode.Criminal&&!Rules.IsSafe(Combat!.Get(id).Position))next=next with {CombatUntil=now+Rules.CombatSeconds*1000L};
        if(next!=s){_pvp[id]=next;DirtyPvp(id);}
    }
    private void PvpDamage(NetworkEntityId source,NetworkEntityId target,double damage)
    {
        if(!_pvp.ContainsKey(target))return;var now=PvpClock();GroundItems!.CancelChannel(target,Tick);
        if(_pvp.TryGetValue(source,out var attacker))
        {
            var defender=_pvp[target];var victimId=_pvpIdentities[target];var episodes=attacker.Episodes.Where(e=>e.Until>now).ToList();
            var unlawful=episodes.Any(e=>e.Victim==victimId&&e.Civilian)||defender.Mode==PvpMode.Peaceful&&defender.CombatUntil<=now&&defender.AggressorUntil<=now;
            if(unlawful)
            {
                if(!episodes.Any(e=>e.Victim==victimId)){episodes.Add(new(victimId,now+Rules.AggressorSeconds*1000L,true));attacker=attacker with {Reputation=Math.Max(-1000,attacker.Reputation-Rules.AttackPenalty)};}
                attacker=attacker with {AggressorUntil=now+Rules.AggressorSeconds*1000L,Episodes=episodes.ToArray()};
            }
            _pvp[source]=attacker with {CombatUntil=now+Rules.CombatSeconds*1000L,ProtectionUntil=0};
            _pvp[target]=defender with {CombatUntil=now+Rules.CombatSeconds*1000L,LastAttacker=_pvpIdentities[source],LastPvpAt=now};
            DirtyPvp(source);DirtyPvp(target);
        }
        if(Combat!.Get(target).Health<=0){_lethal.Add(target);var p=_playersByEntity[target];p.Motion.Reset(p.Position,p.Position);_movingPlayers.Remove(p.ConnectionId);Abilities!.StopDead(target);Echoes?.Wake(target);}
    }
    public bool TryQueuePvp(int connection,PvpCommand c)
    {
        if(!HasPvp||!_playersByConnection.TryGetValue(connection,out var p)||!NetworkProtocol.ValidPvpCommand(c))return false;
        if(_pvpRequests.GetValueOrDefault(connection,uint.MaxValue)==Tick){_pvpResults[p.EntityId]=new(c.Sequence,Tick+1,PvpOutcome.RateLimited);return false;}
        _pvpRequests[connection]=Tick;_pvpPending[connection]=c;return true;
    }
    private void SimulatePvpIntentions()
    {
        if(!HasPvp)return;var now=PvpClock();
        foreach(var id in _pvpTimed.ToArray())
        {
            var s=_pvp[id];var episodes=s.Episodes.Where(e=>e.Until>now).ToArray();var next=s with {CombatUntil=s.CombatUntil>now?s.CombatUntil:0,AggressorUntil=s.AggressorUntil>now?s.AggressorUntil:0,ProtectionUntil=s.ProtectionUntil>now?s.ProtectionUntil:0,Episodes=episodes};
            if(next.CombatUntil!=s.CombatUntil||next.AggressorUntil!=s.AggressorUntil||next.ProtectionUntil!=s.ProtectionUntil||episodes.Length!=s.Episodes.Length){_pvp[id]=next;DirtyPvp(id);}
            if(next.CombatUntil==0&&next.AggressorUntil==0&&next.ProtectionUntil==0&&episodes.Length==0)_pvpTimed.Remove(id);
        }
        foreach(var (connection,c) in _pvpPending)
        {
            var p=_playersByConnection[connection];var id=p.EntityId;var s=_pvp[id];var outcome=PvpOutcome.Accepted;
            if(c.Sequence<=s.LastSequence)outcome=c.Sequence==s.LastSequence&&s.LastAction==(byte)c.Action&&s.LastMode==c.Mode&&s.LastDeathRequest==c.DeathId?PvpOutcome.AlreadyProcessed:PvpOutcome.InvalidOperation;
            else if(c.Action==PvpAction.Mode)
            {
                if(s.Dead||Combat!.Get(id).Health<=0)outcome=PvpOutcome.InvalidState;
                else if(c.Mode!=s.Mode&&(s.CombatUntil>now||Abilities!.HasActiveEffects(id)||Combat!.Get(id).IsCasting))outcome=PvpOutcome.NotReady;
                else if(c.Mode!=PvpMode.Peaceful&&Rules.IsSafe(p.Position))outcome=PvpOutcome.Protected;
                else _pvp[id]=s with {Mode=c.Mode,CombatUntil=c.Mode==PvpMode.Peaceful?s.CombatUntil:now+Rules.CombatSeconds*1000L};
            }
            else if(!s.Dead||s.DeathId!=c.DeathId)outcome=PvpOutcome.InvalidState;
            else if(now<s.RespawnAt)outcome=PvpOutcome.NotReady;
            else RespawnPvp(p);
            // Failed operations also advance the persistent watermark: no stale retry can later mutate state.
            if(c.Sequence>s.LastSequence){_pvp[id]=_pvp[id] with {LastSequence=c.Sequence,LastAction=(byte)c.Action,LastMode=c.Mode,LastDeathRequest=c.DeathId,LastOutcome=outcome};DirtyPvp(id);}
            _pvpResults[id]=new(c.Sequence,Tick,outcome);
        }
        _pvpPending.Clear();
    }
    private void RespawnPvp(ServerPlayer p)
    {
        var id=p.EntityId;var spawn=Rules.Respawn;GroundItems!.CancelChannel(id,Tick);p.Motion.Reset(spawn,spawn);_movingPlayers.Remove(p.ConnectionId);_spatial.Move(id,spawn);Combat!.Move(id,spawn);Combat.Respawn(id);Abilities!.Respawn(id);Echoes?.Respawn(id);
        _pvp[id]=_pvp[id] with {Dead=false,CombatUntil=0,ProtectionUntil=PvpClock()+Rules.ProtectionSeconds*1000L};DirtyPvp(id);
        Audit(_persistentActors.GetValueOrDefault(p.ConnectionId,"runtime"),"Respawn",_pvp[id].DeathId.ToString());
    }
    private void SimulateDeaths()
    {
        if(!HasPvp)return;var now=PvpClock();
        foreach(var id in _lethal)
        {
            var s=_pvp[id];if(s.Dead)continue;var p=_playersByEntity[id];var isPvp=s.LastAttacker!=Guid.Empty&&s.LastPvpAt+Rules.MixedSeconds*1000L>now;
            if(s.DeathId==long.MaxValue)throw new InvalidOperationException("Death ordinal exhausted.");
            if(_playerTrades.TryGetValue(id,out var session)&&_trades.TryGetValue(session,out var trade))CloseTrade(trade,TradePhase.Cancelled);
            _economyQuotes.Remove(p.ConnectionId);_repairQuotes.Remove(p.ConnectionId);
            var xp=_progression[id].Experience;var lost=(int)((long)xp*(isPvp?Rules.PvpExperiencePercent:Rules.PveExperiencePercent)/100);_progression[id]=_progression[id] with {Experience=xp-lost};DirtyProgression(id);
            var dropped=Guid.Empty;var skipped=false;
            if(isPvp)
            {
                var candidates=Inventory!.DeathCandidates(id);
                if(candidates.Length>0)
                {
                    var roll=DeathRoll(candidates.Length);if(roll<0||roll>=candidates.Length)throw new InvalidOperationException("Invalid death RNG.");var selected=candidates[roll];
                    var loot=new SavedDeathLoot(selected with {EquippedSlot=EquipmentSlot.None},p.Position.X,p.Position.Y,now+Rules.LootSeconds*1000L);
                    var next=_nodeState with {DeathLoot=[.._nodeState.DeathLoot??[],loot]};
                    if((_nodeState.DeathLoot?.Length??0)>=8||!GroundItems!.HasDropCapacity||System.Text.Encoding.UTF8.GetByteCount(next.Serialize())>8192)skipped=true;
                    else{var removed=Inventory.RemoveDeathItem(id,selected.InstanceId);if(removed!=loot.Item)throw new InvalidOperationException("Death transfer mismatch.");_nodeState=next;GroundItems!.AddDeathLoot(loot);dropped=selected.InstanceId;}
                }
                foreach(var (killer,identity) in _pvpIdentities)
                {
                    if(identity!=s.LastAttacker)continue;var k=_pvp[killer];
                    if(k.Episodes.Any(e=>e.Victim==_pvpIdentities[id]&&e.Civilian&&e.Until>now)){_pvp[killer]=k with {Reputation=Math.Max(-1000,k.Reputation-Rules.KillPenalty),Pk=k.Pk==int.MaxValue?int.MaxValue:k.Pk+1};DirtyPvp(killer);}break;
                }
            }
            _pvp[id]=s with {Dead=true,PvpDeath=isPvp,DeathId=s.DeathId+1,RespawnAt=now+(isPvp?Rules.PvpRespawnSeconds:Rules.PveRespawnSeconds)*1000L,ExperienceLost=lost,DroppedItem=dropped,DropSkippedCapacity=skipped};DirtyPvp(id);
            Audit(_persistentActors.GetValueOrDefault(p.ConnectionId,"runtime"),"Death",$"{s.DeathId+1}, PvP {isPvp}, EXP {lost}, item {dropped:N}, capacity {skipped}");
        }
        _lethal.Clear();
    }
    private void RestoreDeathLoot()
    {
        if(_nodeState.DeathLoot is not {} items)return;if(!HasPvp)throw new InvalidDataException("Death loot requires PvP module.");
        foreach(var l in items)GroundItems!.AddDeathLoot(l);
    }
    private SavedProgression? CaptureProgression(NetworkEntityId id)=>_progression.TryGetValue(id,out var s)?s with {Pvp=_pvp.GetValueOrDefault(id)}:null;
    public PvpZoneState PublicPvpZone()=>new(Rules.SafeMinX,Rules.SafeMaxX,Rules.SafeMinZ,Rules.SafeMaxZ);
    public PvpPublicState PublicPvp(NetworkEntityId id){var s=_pvp[id];var now=PvpClock();return new(id,Tick,s.Mode,s.CombatUntil>now,s.AggressorUntil>now,s.ProtectionUntil>now,s.Dead);}
    public PvpState PrivatePvp(NetworkEntityId id){var s=_pvp[id];var now=PvpClock();static float Left(long at,long now,float cap)=>(float)Math.Clamp((at-now)/1000d,0,cap);return new(id,Tick,s.LastSequence,s.Mode,s.Reputation,s.Pk,s.DeathId,s.Dead,s.PvpDeath,s.Dead?Left(s.RespawnAt,now,60):0,s.ExperienceLost,s.DroppedItem!=Guid.Empty,s.DropSkippedCapacity,Left(s.CombatUntil,now,120),Left(s.AggressorUntil,now,600));}
    public void ClearPvpResults(){_pvpDirty.Clear();_pvpResults.Clear();}
    private void RemovePvp(int connection,NetworkEntityId id){_pvp.Remove(id);_pvpIdentities.Remove(id);_pvpPending.Remove(connection);_pvpRequests.Remove(connection);_pvpDirty.Remove(id);_pvpTimed.Remove(id);_lethal.Remove(id);_pvpResults.Remove(id);}
    public void DisconnectPvp(int connection){if(!_playersByConnection.TryGetValue(connection,out var p))return;
        ResetQuestSession(p.EntityId);
        Abilities?.SetManaRecoveryConnected(p.EntityId, false); // NC: logout bodies do not recover.
        Combat?.SetDefenseConnected(p.EntityId,false);
        RemoveCraftPlayer(connection,p.EntityId);RemoveRepairPlayer(connection,p.EntityId);RemoveEconomyPlayer(connection,p.EntityId);_nodePending.Remove(connection);_nodeSequences.Remove(connection);_progressionPending.Remove(p.EntityId);_professionPending.Remove(p.EntityId);
GroundItems?.CancelChannel(p.EntityId,Tick);_pvpPending.Remove(connection);if(_playerTrades.TryGetValue(p.EntityId,out var key)&&_trades.TryGetValue(key,out var trade))CloseTrade(trade,TradePhase.Cancelled);p.Motion.Reset(p.Position,p.Position);_movingPlayers.Remove(connection);}
    public ServerPlayer GetPlayer(int connection)=>_playersByConnection[connection];
    public void RebindConnection(int oldConnection,int newConnection,PlayerId player)
    {
        var p=_playersByConnection[oldConnection];_playersByConnection.Remove(oldConnection);p.ConnectionId=newConnection;p.PlayerId=player;_playersByConnection.Add(newConnection,p);
        Abilities?.SetManaRecoveryConnected(p.EntityId, newConnection >= 0); // NC
        Combat?.SetDefenseConnected(p.EntityId,newConnection>=0);
        ResetQuestSession(p.EntityId);
        if(_persistentActors.Remove(oldConnection,out var identity))_persistentActors[newConnection]=identity;
        if(newConnection>=0){p.LastProcessedSequence=0;p.LastPathRequestTick=null;Combat!.ResetSession(p.EntityId);Abilities!.ResetSession(p.EntityId);Inventory!.ResetSession(p.EntityId);Echoes?.ResetSession(p.EntityId);GroundItems!.RemovePlayer(p.EntityId);_progressionSequences.Remove(p.EntityId);_professionSequences.Remove(p.EntityId);_tradeRequests.Remove(oldConnection);}
        _movingPlayers.Remove(oldConnection);_persistenceDirty.Remove(oldConnection);_pvpRequests.Remove(oldConnection);_pvpPending.Remove(oldConnection);MarkPersistent(p.EntityId);
    }
}
