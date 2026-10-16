namespace Content.Server.Combat;

public enum CombatSourceKind : byte { Environment, Character, Echo, NpcLife }

/// <summary>Server-only identity. A runtime network ID is never written into effect persistence.</summary>
public readonly record struct CombatSourceId(CombatSourceKind Kind, Guid Id)
{
    public bool IsValid => Enum.IsDefined(Kind) && (Id != Guid.Empty || Kind == CombatSourceKind.Environment);
}
