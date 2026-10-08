using System.Numerics;
using Content.Server.Data;
namespace Content.Server.Crafting;
public sealed record MaterialDefinition(ushort Id,string Name);
public sealed record MaterialCost(ushort Id,int Quantity);
public sealed record ResourceDefinition(ushort Id,string Name,ushort MaterialId,float X,float Z,int Stock)
{ public Vector2 Position => new(X,Z); }
public sealed record RecipeDefinition(ushort Id,string Name,MaterialCost[] Costs,ushort OutputMaterialId,int OutputQuantity,string? OutputItemId,int Experience);
public sealed record CraftingDefinition
{
    public required string StationName { get; init; }
    public required float StationX { get; init; }
    public required float StationZ { get; init; }
    public required float InteractionRange { get; init; }
    public required float ActionCooldownSeconds { get; init; }
    public required MaterialDefinition[] Materials { get; init; }
    public required ResourceDefinition[] Resources { get; init; }
    public required RecipeDefinition[] Recipes { get; init; }
    public Vector2 StationPosition => new(StationX,StationZ);
    public void Validate(ContentCatalog catalog)
    {
        static bool Name(string? s) => !string.IsNullOrWhiteSpace(s) && s.Length<=24 && System.Text.Encoding.UTF8.GetByteCount(s)<=72 && !s.Any(char.IsControl);
        if(!Name(StationName) || !float.IsFinite(StationX) || !float.IsFinite(StationZ) || !float.IsFinite(InteractionRange) || InteractionRange is < 0.5f or > 3 || !float.IsFinite(ActionCooldownSeconds) || ActionCooldownSeconds is < 0.5f or > 60 || Materials is null || Materials.Length is < 1 or > 16 || Resources is null || Resources.Length is < 1 or > 16 || Recipes is null || Recipes.Length is < 1 or > 8) throw new InvalidDataException("Invalid crafting definition.");
        var materials=new HashSet<ushort>(); var nodes=new HashSet<ushort>(); var recipes=new HashSet<ushort>();
        foreach(var m in Materials) if(m is null || m.Id==0 || !materials.Add(m.Id) || !Name(m.Name)) throw new InvalidDataException("Invalid material.");
        foreach(var n in Resources) if(n is null || n.Id==0 || !nodes.Add(n.Id) || !materials.Contains(n.MaterialId) || !Name(n.Name) || !float.IsFinite(n.X) || !float.IsFinite(n.Z) || n.Stock is < 1 or > 1000) throw new InvalidDataException("Invalid resource node.");
        foreach(var r in Recipes)
        {
            if(r is null || r.Id==0 || !recipes.Add(r.Id) || !Name(r.Name) || r.Costs is null || r.Costs.Length is < 1 or > 4 || r.Experience is < 0 or > 100 || (r.OutputItemId is null ? !materials.Contains(r.OutputMaterialId) || r.OutputQuantity is < 1 or > 100 : r.OutputMaterialId!=0 || r.OutputQuantity!=1 || !catalog.Items.ContainsKey(r.OutputItemId))) throw new InvalidDataException("Invalid recipe output.");
            var seen=new HashSet<ushort>(); foreach(var c in r.Costs) if(c is null || !materials.Contains(c.Id) || !seen.Add(c.Id) || c.Quantity is < 1 or > 100) throw new InvalidDataException("Invalid recipe cost.");
        }
    }
}
