namespace Content.Shared.Network;

/// <summary>
/// Opaque server-issued runtime ID, separate from persistent database IDs.
/// </summary>
public readonly record struct NetworkEntityId(ulong Value)
{
    public static readonly NetworkEntityId Invalid = new(0);
    public bool IsValid => Value != 0;
    public override string ToString() => Value.ToString();
}
