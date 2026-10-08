using System.Text.Json.Serialization;
namespace Content.Server.Persistence;
public sealed record SavedItemCondition
{
    [JsonRequired] public int Version { get; init; }=1;
    public required int Current { get; init; }
    public required int Maximum { get; init; }
    public required ulong Revision { get; init; }
    public void Validate() { if(Version!=1 || Maximum is < 1 or > 1000 || Current<0 || Current>Maximum || Revision is 0 or > long.MaxValue) throw new InvalidDataException("Invalid item condition."); }
}
/// <summary>One bounded durable receipt + monotonic watermark, atomically saved with materials/items.</summary>
public sealed record SavedMaintenance
{
    [JsonRequired] public int Version { get; init; }=1;
    public required ulong LastOperation { get; init; }
    public required string PayloadHash { get; init; }
    public required Guid ItemId { get; init; }
    public required ulong ItemRevision { get; init; }
    public required ushort MaterialId { get; init; }
    public required ushort Quantity { get; init; }
    public void Validate() { if(Version!=1 || LastOperation is 0 or > long.MaxValue || ItemRevision is 0 or > long.MaxValue || ItemId==Guid.Empty || MaterialId==0 || Quantity is 0 or > 1000 || PayloadHash is not {Length:64} || PayloadHash.Any(c=>!char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid maintenance receipt."); }
}
