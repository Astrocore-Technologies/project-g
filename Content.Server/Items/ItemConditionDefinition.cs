namespace Content.Server.Items;
public sealed record ItemConditionDefinition
{
    public required int Maximum { get; init; }
    public required int WearPerHit { get; init; }
    public required ushort RepairMaterialId { get; init; }
    public required int DurabilityPerMaterial { get; init; }
    public void Validate() { if(Maximum is < 1 or > 1000 || WearPerHit is < 1 or > 100 || WearPerHit>Maximum || RepairMaterialId==0 || DurabilityPerMaterial is < 1 or > 1000) throw new ArgumentException("Invalid item condition definition."); }
}
