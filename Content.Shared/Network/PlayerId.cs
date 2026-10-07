namespace Content.Shared.Network;

/// <summary>
/// Identifies an authenticated player for the lifetime of a server process.
/// </summary>
public readonly record struct PlayerId(ulong Value)
{
    public static readonly PlayerId Invalid = new(0);
    public bool IsValid => Value != 0;
    public override string ToString() => Value.ToString();
}
