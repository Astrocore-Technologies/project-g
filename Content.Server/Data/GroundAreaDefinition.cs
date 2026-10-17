namespace Content.Server.Data;

public enum GroundAreaShape : byte { Cylinder=1 }

/// <summary>Server-authored volume and propagation; independent of the selected ground anchor.</summary>
public sealed record GroundAreaDefinition
{
    public required GroundAreaShape Shape {get;init;}
    public required float Height {get;init;}
    public required bool RequiresConnectedSurface {get;init;}
    public void Validate()
    {
        if(Shape!=GroundAreaShape.Cylinder || !float.IsFinite(Height) || Height is <=0 or >128)
            throw new ArgumentException("Ground area requires a cylinder height in (0,128].");
    }
}
