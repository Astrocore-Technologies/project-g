namespace Content.Server.Persistence;

/// <summary>Additive v1 extension. A saved floor is meaningful only in its exact exported region revision.</summary>
public sealed record SavedSurface(int Version, float Height, ulong GeometryHash)
{
    public void Validate()
    {
        if (Version != 1 || !float.IsFinite(Height) || MathF.Abs(Height) > 128 || GeometryHash == 0)
            throw new InvalidDataException("Unsupported saved surface; geometry migration is required.");
    }
}
