using Content.Shared.Combat;

namespace Content.Server.Data;

public enum ControlOrigin : byte { Source, Impact }

/// <summary>One authored control payload. Its parameters are independent of action form and hit geometry.</summary>
public sealed record SkillControlDefinition
{
    public required CrowdControlKind Kind { get; init; }
    public double? DurationSeconds { get; init; }
    public double? Strength { get; init; }
    public float? Distance { get; init; }
    public float? Speed { get; init; }
    public ControlOrigin? Origin { get; init; }
    public float? Height { get; init; }
    public double? DownSeconds { get; init; }

    internal void Validate(string path)
    {
        if(!Enum.IsDefined(Kind)) throw new ArgumentException($"{path}: unknown control kind.");
        var displacement=Kind is CrowdControlKind.Knockback or CrowdControlKind.Pull;
        var launch=Kind==CrowdControlKind.Knockup;
        var strengthBased=Kind is CrowdControlKind.Slow or CrowdControlKind.Blind or CrowdControlKind.Taunt;
        if(displacement
            ? DurationSeconds is not null || Distance is not {} distance || !float.IsFinite(distance) || distance<=0 ||
                Speed is not {} speed || !float.IsFinite(speed) || speed<=0 || !float.IsFinite(distance/speed) || distance/speed<=0 ||
                Origin is not {} origin || !Enum.IsDefined(origin)
            : DurationSeconds is not {} duration || !double.IsFinite(duration) || duration<=0 || Distance is not null || Speed is not null || Origin is not null)
            throw new ArgumentException($"{path}: invalid duration or displacement geometry.");
        if(strengthBased
            ? Strength is not {} strength || !double.IsFinite(strength) || strength<=0 || Kind!=CrowdControlKind.Blind && strength>1
            : Strength is not null)
            throw new ArgumentException($"{path}: strength is required only for Slow, Blind and Taunt.");
        if(launch
            ? Height is not {} height || !float.IsFinite(height) || height<=0 || DownSeconds is not {} down || !double.IsFinite(down) || down<0 ||
                !double.IsFinite(DurationSeconds!.Value+down)
            : Height is not null || DownSeconds is not null)
            throw new ArgumentException($"{path}: flight geometry is required only for Knockup.");
    }
}
