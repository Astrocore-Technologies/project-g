using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;
using Xunit;

namespace Content.Tests.Server.Data;

public sealed class MeleeSkillDefinitionTests
{
    [Theory][InlineData("origin")][InlineData("thickness")][InlineData("targets")][InlineData("arc")]
    [InlineData("thrust_thickness")][InlineData("thrust_radius")][InlineData("recovery_volume")]
    public void MissingOrContradictoryAuthoredMeleeGeometryFailsBeforeServerStarts(string field)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        JsonNode Skill(string id)=>data["abilities"]!.AsArray().Single(n=>n!["id"]!.GetValue<string>()==id)!;
        var arc=Skill("sword_rend")["melee"]!;
        switch(field)
        {
            case "origin":arc.AsObject().Remove("originHeight");break;
            case "thickness":arc["thickness"]=0;break;
            case "targets":arc.AsObject().Remove("firstTargetOnly");break;
            case "arc":arc.AsObject().Remove("arcDegrees");break;
            case "thrust_thickness":Skill("sword_thrust")["melee"]!["thickness"]=.3;break;
            case "thrust_radius":Skill("sword_thrust")["radius"]=4;break;
            case "recovery_volume":Skill("sword_breath")["melee"]!["originHeight"]=1;break;
        }
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
    }
}
