namespace Content.Server.Data;

/// <summary>Weapon-authored delivery, separate from damage stats and the ordinary action's phases.</summary>
public sealed record BasicProjectileDefinition
{
    public required float Speed {get;init;}
    public required float Radius {get;init;}
    public required float OriginHeight {get;init;}
    public required EffectDamageFamily DamageFamily {get;init;}

    internal void Validate(string path)
    {
        if(!float.IsFinite(Speed) || Speed<=0 || !float.IsFinite(Radius) || Radius is <=0 or >64 ||
            !float.IsFinite(OriginHeight) || OriginHeight is <0 or >128 || !Enum.IsDefined(DamageFamily))
            throw new ArgumentException($"{path}: invalid projectile speed, radius, origin or damage family.");
    }
}
