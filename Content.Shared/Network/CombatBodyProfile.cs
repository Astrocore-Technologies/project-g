namespace Content.Shared.Network;

public enum CombatBodyShape : byte { None, Capsule }

/// <summary>Visible stable body geometry only, independent of movement collision and animated bones.</summary>
public readonly record struct CombatBodyProfile(CombatBodyShape Shape,float Height,float Radius)
{
    public const float MaximumHeight=512;
    public bool IsValid=>Shape==CombatBodyShape.None ? this==default : Shape==CombatBodyShape.Capsule &&
        float.IsFinite(Height) && float.IsFinite(Radius) && Height is >0 and <=MaximumHeight && Radius>0 && 2d*Radius<=Height;
}
