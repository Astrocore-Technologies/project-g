using System.Text.Json;
using System.Text.Json.Serialization;

namespace Content.Server.Progression;

/// <summary>Explicit temporary stand balance; not a final activity or level scaling formula.</summary>
public sealed record ExperienceRewardsDefinition(int Version,int Revision,int PveExperience,float PveRadius,
    int ExplorationCells,int ExplorationExperience)
{
    public int OreExperience {get;init;}
    public int WatchExperience {get;init;}
    public void Validate()
    {
        if(Version!=1 || Revision<1 || PveExperience is <1 or >1000000 || !float.IsFinite(PveRadius) || PveRadius is <1 or >1000 ||
            ExplorationCells is <1 or >1000000 || ExplorationExperience is <1 or >1000000 || OreExperience is <0 or >1000000 || WatchExperience is <0 or >1000000)
            throw new InvalidDataException("Invalid EXP reward configuration.");
    }
    public static ExperienceRewardsDefinition LoadFile(string path)
    {
        var bytes=File.ReadAllBytes(path);if(bytes.Length>4096)throw new InvalidDataException("Oversized EXP reward configuration.");
        var value=JsonSerializer.Deserialize<ExperienceRewardsDefinition>(bytes,new JsonSerializerOptions
        {PropertyNameCaseInsensitive=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectRequiredConstructorParameters=true,MaxDepth=3})
            ??throw new InvalidDataException("Missing EXP reward configuration.");
        value.Validate();return value;
    }
}
