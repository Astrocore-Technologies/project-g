using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Crafting;
using Content.Server.Persistence;
using Content.Shared.Network;
namespace Content.Server.World;
public sealed partial class ServerWorld
{
    private CraftingDefinition? _crafting;
    private SavedResourceStock[] _resourceStock=[];
    private readonly Dictionary<int,CraftCommand> _craftPending=new();
    private readonly Dictionary<int,uint> _craftRequestTicks=new();
    private readonly Dictionary<NetworkEntityId,CraftResult> _craftResults=new();
    private readonly HashSet<ushort> _resourceDirty=new();
    private SpatialIndex? _resourceSpatial;
    public bool HasCrafting => _crafting is not null;
    public IReadOnlyDictionary<NetworkEntityId,CraftResult> CraftResults => _craftResults;
    private void InitializeCrafting(CraftingOptions? options,CraftingDefinition? definition)
    {
        if(options?.Enabled!=true) return;
        if(definition is null || Inventory is null || !HasWorldNode) throw new ArgumentException("Crafting requires content, inventory and a persistent shared region lease.");
        _crafting=definition; _resourceSpatial=new(_interest.CellSize);
        if(!Navigation.IsWalkable(definition.StationPosition) || definition.Resources.Any(n=>!Navigation.IsWalkable(n.Position))) throw new ArgumentException("Crafting positions must be walkable.");
        foreach(var node in definition.Resources) _resourceSpatial.Add(new(node.Id),node.Position);
        RestoreCraftResources();
    }
    private void RestoreCraftResources()
    {
        if(_crafting is null) return;
        if(_nodeState.Resources is { } saved && (saved.Length!=_crafting.Resources.Length || _crafting.Resources.Any(n=>!saved.Any(s=>s.Id==n.Id && s.Remaining<=n.Stock)))) throw new InvalidDataException("Resource definitions changed; migration required.");
        _resourceStock=_crafting.Resources.Select(n=>new SavedResourceStock(n.Id,_nodeState.Resources?.Single(s=>s.Id==n.Id).Remaining ?? n.Stock)).ToArray();
    }
    public CraftRecipeState[] PublicCraftRecipes() => _crafting!.Recipes.Select(r=>new CraftRecipeState(r.Id,r.Name,
        r.OutputItemId is { } item ? _progressionCatalog!.Items[item].Name : _crafting.Materials.Single(m=>m.Id==r.OutputMaterialId).Name,
        _crafting.StationPosition,_crafting.StationName,_crafting.InteractionRange,r.Costs.Select(c=>new CraftIngredient(c.Id,(ushort)c.Quantity,_crafting.Materials.Single(m=>m.Id==c.Id).Name)).ToArray())).ToArray();
    public CraftState CraftState(NetworkEntityId id)
    { var saved=Inventory!.CraftingState(id); return new(id,Tick,saved.LastOperation,(float)saved.CooldownSeconds,saved.Materials.Select(m=>new MaterialAmount(m.Id,(ushort)m.Quantity)).ToArray()); }
    public ResourceNodeState ResourceState(ushort id)
    { var node=Array.Find(_crafting!.Resources,n=>n.Id==id)!; var stock=Array.Find(_resourceStock,n=>n.Id==id)!; return new(id,Tick,node.Position,node.MaterialId,(ushort)stock.Remaining,node.Name); }
    public bool TryQueueCraft(int connection,CraftCommand command)
    {
        if(!HasCrafting || !_playersByConnection.TryGetValue(connection,out var player) || command.Operation is 0 or > long.MaxValue || command.Target==0 || !Enum.IsDefined(command.Action)) return false;
        if(_craftRequestTicks.TryGetValue(connection,out var tick) && tick==Tick)
        { _craftPending.Remove(connection); _craftResults[player.EntityId]=new(command.Operation,Tick+1,CraftOutcome.RateLimited); return false; }
        _craftRequestTicks[connection]=Tick; _craftPending[connection]=command; return true;
    }
    private void SimulateCrafting()
    {
        if(_crafting is null) return;
        foreach(var (connection,command) in _craftPending)
        {
            var player=_playersByConnection[connection]; var actor=Combat!.Get(player.EntityId);
            var outcome=Inventory!.CheckCraftOperation(player.EntityId,command.Operation);
            if(outcome==CraftOutcome.Accepted)
            {
                if(_nodeAudit.Count>=128) outcome=CraftOutcome.RateLimited;
                else if(actor.Health<=0) outcome=CraftOutcome.InvalidState;
                else if(actor.IsCasting || player.Motion.IsMoving || player.Motion.IsDashing || Abilities!.HasActiveEffects(player.EntityId)) outcome=CraftOutcome.Busy;
                else
                {
                    var resource=command.Action==CraftAction.Gather ? Array.Find(_crafting.Resources,n=>n.Id==command.Target) : null;
                    var recipe=command.Action==CraftAction.Make ? Array.Find(_crafting.Recipes,r=>r.Id==command.Target) : null;
                    if(resource is null && recipe is null) outcome=CraftOutcome.Unavailable;
                    else
                    {
                        var position=resource?.Position ?? _crafting.StationPosition;
                        if(Vector2.DistanceSquared(player.Position,position)>_crafting.InteractionRange*_crafting.InteractionRange) outcome=CraftOutcome.TooFar;
                        else if(!Navigation.CanTraverse(player.Position,position)) outcome=CraftOutcome.Blocked;
                        else if(resource is { } node)
                        {
                            var index=Array.FindIndex(_resourceStock,s=>s.Id==node.Id);
                            if(_resourceStock[index].Remaining==0) outcome=CraftOutcome.Depleted;
                            else
                            {
                                outcome=Inventory.GatherMaterial(player.EntityId,command.Operation,node.MaterialId,_crafting.ActionCooldownSeconds);
                                if(outcome==CraftOutcome.Accepted)
                                { var next=(SavedResourceStock[])_resourceStock.Clone(); next[index]=next[index] with { Remaining=next[index].Remaining-1 }; _resourceStock=next; _nodeState=_nodeState with { Resources=next }; _resourceDirty.Add(node.Id); }
                            }
                        }
                        else
                        { outcome=Inventory.MakeRecipe(player.EntityId,command.Operation,recipe!,_crafting.ActionCooldownSeconds); if(outcome==CraftOutcome.Accepted) GrantExperience(player.EntityId,recipe!.Experience); }
                    }
                }
            }
            if(outcome==CraftOutcome.Accepted)
            { MarkPersistent(player.EntityId); Audit(_persistentActors.GetValueOrDefault(connection,"runtime-"+connection),command.Action.ToString(),$"Operation {command.Operation}, target {command.Target}"); }
            _craftResults[player.EntityId]=new(command.Operation,Tick,outcome);
        }
        _craftPending.Clear();
    }
    public void UpdateResourceInterest(int connection,InterestView view)
    {
        view.ResourceEntered.Clear(); view.ResourceLeft.Clear(); if(_crafting is null) return;
        var position=_playersByConnection[connection].Position; _resourceSpatial!.Query(position,_interest.ExitRadius,view.ResourceCandidates);
        foreach(var id in view.ResourceVisible) if(!view.ResourceCandidates.Contains(new(id))) view.ResourceLeft.Add(id);
        foreach(var id in view.ResourceLeft) view.ResourceVisible.Remove(id);
        foreach(var candidate in view.ResourceCandidates)
        { var id=(ushort)candidate.Value; if(!view.ResourceVisible.Contains(id) && Vector2.DistanceSquared(position,ResourceState(id).Position)<=_interest.Radius*_interest.Radius) { view.ResourceVisible.Add(id); view.ResourceEntered.Add(id); } }
    }
    public bool IsResourceDirty(ushort id) => _resourceDirty.Contains(id);
    public void ClearCraftResults() { _craftResults.Clear(); _resourceDirty.Clear(); }
    private void RemoveCraftPlayer(int connection,NetworkEntityId id) { _craftPending.Remove(connection); _craftRequestTicks.Remove(connection); _craftResults.Remove(id); }
}
