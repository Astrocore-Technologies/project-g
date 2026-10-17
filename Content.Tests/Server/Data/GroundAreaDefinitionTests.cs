using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;
using Xunit;

namespace Content.Tests.Server.Data;

public sealed class GroundAreaDefinitionTests
{
    [Theory][InlineData("missing")][InlineData("shape")][InlineData("height")][InlineData("connected")]
    [InlineData("zero")][InlineData("large")][InlineData("unexpected")]
    public void MissingOrUnsupportedVolumeFailsCatalogValidation(string field)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var skill=data["abilities"]![1]!;
        switch(field)
        {
            case "missing":skill.AsObject().Remove("groundVolume");break;
            case "shape":skill["groundVolume"]!["shape"]="Sphere";break;
            case "height":skill["groundVolume"]!.AsObject().Remove("height");break;
            case "connected":skill["groundVolume"]!.AsObject().Remove("requiresConnectedSurface");break;
            case "zero":skill["groundVolume"]!["height"]=0;break;
            case "large":skill["groundVolume"]!["height"]=129;break;
            case "unexpected":data["abilities"]![0]!["groundVolume"]=skill["groundVolume"]!.DeepClone();break;
        }
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
    }
}
