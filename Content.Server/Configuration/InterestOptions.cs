namespace Content.Server.Configuration;

/// <summary>Development AOI settings; exit hysteresis prevents boundary flicker.</summary>
public sealed class InterestOptions
{
    public const string SectionName = "Interest";
    public float CellSize { get; init; } = 8f;
    public float Radius { get; init; } = 12f;
    public float ExitRadius { get; init; } = 14f;

    public bool IsValid() =>
        float.IsFinite(CellSize) && CellSize > 0f &&
        float.IsFinite(Radius) && Radius > 0f &&
        float.IsFinite(ExitRadius) && ExitRadius >= Radius &&
        ExitRadius / CellSize <= 32f && ExitRadius <= 10000f;
}
