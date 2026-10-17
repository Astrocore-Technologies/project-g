using System.Collections.Frozen;

namespace Content.Server.World;

/// <summary>Decision from the server relationship system. No thresholds, reasons, RNG or expiry rules live here.</summary>
public interface IEchoCombatPermissionSource
{
    EchoCombatPermission Read(Guid echoInstanceId);
}

public sealed class EchoCombatPermission(bool basicAllowed=true,bool skillsAllowed=true,bool signatureAllowed=true,
    IEnumerable<ushort>? refusedSkills=null)
{
    public static EchoCombatPermission AllowAll {get;}=new();
    public bool BasicAllowed {get;}=basicAllowed;
    public bool SkillsAllowed {get;}=skillsAllowed;
    public bool SignatureAllowed {get;}=signatureAllowed;
    public FrozenSet<ushort> RefusedSkills {get;}=(refusedSkills??[]).ToFrozenSet();
}
