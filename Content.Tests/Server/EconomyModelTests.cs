using System.Text.Json.Nodes;
using System.Text;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;
namespace Content.Tests.Server;
public sealed class EconomyModelTests
{
    [Fact] public void LegacyIsOptionalAndCorruptReceiptEscrowAndDeadlineFailClosed()
    {
        var w=CraftingTests.World();var old=w.CreateInitialCharacter().Inventory!;Assert.Null(SavedInventory.Deserialize(old.Serialize()).Economy);var saved=EconomyTests.Initial(w).Inventory!;var json=JsonNode.Parse(saved.Serialize())!;json["Economy"]=null;Assert.Throws<InvalidDataException>(()=>SavedInventory.Deserialize(json.ToJsonString()));json=JsonNode.Parse(saved.Serialize())!;json["Economy"]!["Coins"]=-1;Assert.Throws<InvalidDataException>(()=>SavedInventory.Deserialize(json.ToJsonString()));json=JsonNode.Parse(saved.Serialize())!;json["Items"]![0]!["Enhancement"]=6;Assert.Throws<InvalidDataException>(()=>SavedInventory.Deserialize(json.ToJsonString()));
        var item=saved.Items[0];var seller=Guid.NewGuid();var listing=new SavedListing(1,seller,item,10);Assert.Throws<InvalidDataException>(()=>new SavedMarket{NextListing=2,Listings=[listing,listing],Credits=[]}.Validate());Assert.Throws<InvalidDataException>(()=>new SavedMarket{NextListing=2,Listings=[listing],Credits=[new(seller,1),new(seller,1)]}.Validate());Assert.Throws<InvalidDataException>(()=>new SavedWorldNode{Resources=[new(1,1){RefillAt=1600}]}.Validate());Assert.Throws<InvalidDataException>(()=>SavedWorldNode.Deserialize("{\"Version\":1,\"Repairs\":0,\"Patrols\":0,\"StormRumor\":false,\"Market\":null}"));
    }
    [Fact] public void BalanceReferencesChancesAndBudgetsAreValidatedBeforeStartup()
    {
        foreach(var change in new Action<JsonNode>[]{n=>n["enhancementChances"]![0]=101,n=>n["listingLimit"]=9,n=>n["oreId"]=999,n=>n["evolvedWeapon"]="missing",n=>n["refillSeconds"]=0})
        {var json=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;change(json["economy"]!);Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString())));}
    }
}
