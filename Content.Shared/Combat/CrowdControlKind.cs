namespace Content.Shared.Combat;

public enum CrowdControlKind : byte
{
    Stun, Root, Slow, Silence, Disarm, Knockback, Knockdown, Fear, Sleep, Blind, Taunt, Pull, Freeze,
    Knockup
}

public enum ControlCategory : byte { Hard, Movement, Action, Soft, Targeting }
