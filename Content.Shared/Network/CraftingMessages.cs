using System.Numerics;
namespace Content.Shared.Network;
public enum CraftAction : byte { Gather=1, Make=2 }
public enum CraftOutcome : byte { Accepted, AlreadyProcessed, InvalidOperation, InvalidState, Busy, TooFar, Blocked, Unavailable, Depleted, MissingMaterials, InventoryFull, MaterialFull, Cooldown, RateLimited }
public readonly record struct CraftCommand(ulong Operation,CraftAction Action,ushort Target);
public readonly record struct CraftResult(ulong Operation,uint ServerTick,CraftOutcome Outcome);
public readonly record struct MaterialAmount(ushort Id,ushort Quantity);
public readonly record struct CraftState(NetworkEntityId OwnerId,uint ServerTick,ulong LastOperation,float CooldownSeconds,IReadOnlyList<MaterialAmount> Materials);
public readonly record struct ResourceNodeState(ushort Id,uint ServerTick,Vector2 Position,ushort MaterialId,ushort Remaining,string Name);
public readonly record struct ResourceNodeDespawn(ushort Id,uint ServerTick);
public readonly record struct CraftIngredient(ushort Id,ushort Quantity,string Name);
public readonly record struct CraftRecipeState(ushort Id,string Name,string OutputName,Vector2 StationPosition,string StationName,float InteractionRange,IReadOnlyList<CraftIngredient> Costs);
