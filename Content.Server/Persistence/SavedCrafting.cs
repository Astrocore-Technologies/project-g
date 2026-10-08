using System.Text.Json.Serialization;
namespace Content.Server.Persistence;
public sealed record SavedMaterial(ushort Id,int Quantity);
public sealed record SavedCrafting
{
    [JsonRequired] public int Version { get; init; }=1;
    public required ulong LastOperation { get; init; }
    public required double CooldownSeconds { get; init; }
    public required SavedMaterial[] Materials { get; init; }
    public static SavedCrafting Empty => new() { LastOperation=0,CooldownSeconds=0,Materials=[] };
    public void Validate()
    {
        if(Version!=1 || LastOperation>long.MaxValue || !double.IsFinite(CooldownSeconds) || CooldownSeconds is < 0 or > 60 || Materials is null || Materials.Length>16) throw new InvalidDataException("Invalid crafting state.");
        var ids=new HashSet<ushort>(); foreach(var m in Materials) if(m is null || m.Id==0 || !ids.Add(m.Id) || m.Quantity is < 1 or > 999) throw new InvalidDataException("Invalid material stack.");
    }
}
public sealed record SavedResourceStock([property: JsonRequired] ushort Id,[property: JsonRequired] int Remaining);
