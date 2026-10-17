namespace Content.Server.Data;

/// <summary>Technical soft avoidance tuning, independent of attack/hurtbox dimensions.</summary>
public sealed record MovementAvoidanceDefinition
{
    public float Radius {get;init;}=.4f;
    public float LateralFraction {get;init;}=.35f;
    public float HorizonSeconds {get;init;}=.1f;
    internal void Validate(string id)
    {
        if(!float.IsFinite(Radius)||Radius is <=0 or >4 || !float.IsFinite(LateralFraction)||LateralFraction is <0 or >.5f ||
            !float.IsFinite(HorizonSeconds)||HorizonSeconds is <=0 or >.5f)
            throw new ArgumentException($"creature {id}: invalid soft avoidance geometry or steering.");
    }
}
