using Content.Shared.Combat;

namespace Content.Server.Data;

/// <summary>Remove all matching, eligible instances. Control kinds are an explicit permission list, never a wildcard.</summary>
public sealed record SkillRemovalDefinition
{
    public required bool Timed { get; init; }
    public required CrowdControlKind[] Controls { get; init; }
    public string[]? Tags { get; init; }
    public string[]? ControlTags {get;init;}

    internal void Validate(bool cleanse,string path)
    {
        if(Controls is null || Controls.Any(k=>!Enum.IsDefined(k)) || Controls.Distinct().Count()!=Controls.Length ||
            !Timed && Controls.Length==0 || !cleanse && Controls.Length>0 || !Timed && Tags is not null)
            throw new ArgumentException($"{path}: invalid timed/control removal selection.");
        if(Tags is not null)
        {
            EffectDefinition.ValidateTags(Tags);
            if(Tags.Length==0) throw new ArgumentException($"{path}: use null for all eligible tags or a nonempty filter.");
        }
        if(ControlTags is not null)
        {
            if(!cleanse || Controls.Length==0 || ControlTags.Length==0)
                throw new ArgumentException($"{path}: control tag filtering requires an explicit cleanse control selection.");
            EffectDefinition.ValidateTags(ControlTags);
        }
    }
}
