using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;
using Xunit;

namespace Content.Tests.Server.Data;

public sealed class DodgeDefinitionTests
{
    [Theory][InlineData("missing")][InlineData("duration")][InlineData("protection")][InlineData("threats")]
    [InlineData("overlong")][InlineData("speed")][InlineData("unexpected")][InlineData("professionCost")][InlineData("turn")]
    public void MissingOrContradictoryDodgeDataFailsBeforeTheServerStarts(string invalid)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var skill=data["abilities"]![2]!;
        switch(invalid)
        {
            case "missing":skill.AsObject().Remove("dodge");break;
            case "duration":skill["dodge"]!.AsObject().Remove("durationSeconds");break;
            case "protection":skill["dodge"]!["protectionSeconds"]=-1;break;
            case "threats":skill["dodge"]!["protectedThreats"]="Unknown";break;
            case "overlong":skill["dodge"]!["protectionSeconds"]=.3;break;
            case "speed":skill["speed"]=15;break;
            case "unexpected":data["abilities"]![0]!["dodge"]=skill["dodge"]!.DeepClone();break;
            case "professionCost":data["professions"]![1]!["dodge"]!["staminaCost"]=-1;break;
            case "turn":skill["action"]!["active"]!["canTurn"]=true;break;
        }
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
    }
}
