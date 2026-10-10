using System.Numerics;

namespace Content.Shared.Network;

public enum AbilityAreaShape : byte { None, Circle, Sector, Corridor, Self }

/// <summary>Public footprint derived from execution rules, never from a client-side skill ID table.</summary>
public readonly record struct AbilityArea(AbilityAreaShape Shape, float Length = 0, float HalfWidth = 0,
    float HalfAngleRadians = 0, bool Stationary = false)
{
    public const int WireBytes = 14;
    public bool IsValid => Enum.IsDefined(Shape) && float.IsFinite(Length) && Length is >= 0 and <= 512 &&
        float.IsFinite(HalfWidth) && HalfWidth is >= 0 and <= 64 &&
        float.IsFinite(HalfAngleRadians) && HalfAngleRadians is >= 0 and <= MathF.PI && Shape switch
        {
            AbilityAreaShape.None => this == default,
            AbilityAreaShape.Self => Length == 0 && HalfWidth == 0 && HalfAngleRadians == 0,
            AbilityAreaShape.Circle => Length > 0 && HalfWidth == 0 && HalfAngleRadians == 0,
            AbilityAreaShape.Sector => Length > 0 && HalfWidth == 0 && HalfAngleRadians > 0,
            AbilityAreaShape.Corridor => Length > 0 && HalfWidth > 0 && HalfAngleRadians == 0,
            _ => false
        };

    // The vertical offset spends range exactly as it does in the authoritative combat checks.
    public bool Contains(Vector3 offset, Vector2 direction)
    {
        if (offset.LengthSquared() > Length * Length + .00001f) return false;
        var planar = new Vector2(offset.X, offset.Z);
        var along = Vector2.Dot(planar, direction);
        return Shape switch
        {
            AbilityAreaShape.Circle => true,
            AbilityAreaShape.Sector => planar.LengthSquared() < .000001f ||
                HalfAngleRadians >= MathF.PI || along >= MathF.Sqrt(planar.LengthSquared()) * MathF.Cos(HalfAngleRadians),
            AbilityAreaShape.Corridor => along >= 0 &&
                MathF.Abs(planar.X * direction.Y - planar.Y * direction.X) <= HalfWidth,
            _ => false
        };
    }
}
