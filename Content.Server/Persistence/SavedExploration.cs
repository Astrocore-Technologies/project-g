using System.Text.Json.Serialization;
using Content.Shared.Network;
namespace Content.Server.Persistence;
/// <summary>Private bounded cartography. Geometry identifies the layout; unknown layouts require migration.</summary>
public sealed record SavedExploration
{
    [JsonRequired] public int Version { get; init; }=1;
    public required string RegionKey { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required float OriginX { get; init; }
    public required float OriginZ { get; init; }
    public required float CellSize { get; init; }
    public required ulong[] Cells { get; init; }
    public required byte Tutorial { get; init; }
    // Absent in legacy saves: their existing discovery bits belong to the original region.
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public byte? Places { get; init; }
    // Only newly revealed walkable cells during movement, never retroactive credit for a legacy map.
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] public int ExperienceCells {get;init;}
    public void Validate()
    {
        var count=(long)Width*Height;
        if(ExperienceCells<0 || ExperienceCells>count)throw new InvalidDataException("Invalid exploration EXP receipt.");
        if(Version!=1 || !ValidRegion(RegionKey) || Width<1 || Height<1 || count>NetworkConstants.MaxNavigationCells || !float.IsFinite(OriginX) || !float.IsFinite(OriginZ) || !float.IsFinite(CellSize) || CellSize is < 0.1f or > 100 || Cells is null || Cells.Length!=(count+63)/64 || Tutorial>127 || Places>3 || ((count%64)!=0 && Cells[^1]>>(int)(count%64)!=0)) throw new InvalidDataException("Invalid exploration model.");
        if(ExperienceCells>0)
        {
            var revealed=0;foreach(var bits in Cells)revealed+=System.Numerics.BitOperations.PopCount(bits);
            if(ExperienceCells>revealed)throw new InvalidDataException("Exploration EXP receipt exceeds revealed cells.");
        }
    }
    private static bool ValidRegion(string region)
    {
        if (string.IsNullOrEmpty(region) || region.Length > 64) return false;
        foreach (var c in region)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) return false;
        return true;
    }
}
