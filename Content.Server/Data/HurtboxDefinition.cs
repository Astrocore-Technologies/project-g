namespace Content.Server.Data;

public enum HurtboxShape : byte { Capsule = 1 }

/// <summary>Total dimensions of the stable combat body, independent of navigation and animation.</summary>
public sealed record HurtboxDefinition
{
    public required HurtboxShape Shape { get; init; }
    public required float Height { get; init; }
    public required float Radius { get; init; }

    // Knockdown can rotate the entire capsule into the horizontal plane.
    internal float MaximumHorizontalReach => Height * .5f;

    internal void Validate(string name)
    {
        if (Shape != HurtboxShape.Capsule || !float.IsFinite(Height) || !float.IsFinite(Radius) ||
            Height>Content.Shared.Network.CombatBodyProfile.MaximumHeight || Radius <= 0 || Height < 2d * Radius)
            throw new ArgumentException($"{name}: expected a finite capsule with height >= 2 * radius > 0.");
    }
}
