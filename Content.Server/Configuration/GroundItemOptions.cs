namespace Content.Server.Configuration;

public sealed class GroundItemOptions
{
    public const string SectionName = "GroundItems";
    public bool Enabled { get; set; }
    public float PickupRange { get; set; } = 2;
    public GroundItemSeed[] Seeds { get; set; } = [];
}
public sealed class GroundItemSeed
{
    public string Id { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
}
