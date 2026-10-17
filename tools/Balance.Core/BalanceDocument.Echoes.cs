using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;

namespace ProjectG.Balance;

public sealed partial class BalanceDocument
{
    /// <summary>Rarity and the explicit five-star action reaction change in one validated undo step.</summary>
    public void ConfigureEchoRefusal(string path,byte? stars,EchoRefusalActionReaction? reaction)
    {
        if(!path.StartsWith("echoes/",StringComparison.Ordinal)||path.Count(c=>c=='/')!=1||
            stars is <1 or >5||(stars==5)!=(reaction is not null)||reaction is {} value&&!Enum.IsDefined(value))
            throw new ArgumentException("Для 5★ явно выберите реакцию на начатое действие; другие Эхо не используют отказ.");
        var next=(JsonObject)Root.DeepClone();
        if(Resolve(next,path) is not JsonObject echo)throw new ArgumentException("Не выбран боевой профиль Эхо.");
        if(stars is {} rarity)echo["stars"]=(int)rarity;else echo.Remove("stars");
        if(reaction is {} action)echo["refusalDuringAction"]=action.ToString();else echo.Remove("refusalDuringAction");
        _=ContentCatalog.Parse(Encoding.UTF8.GetBytes(next.ToJsonString()));
        if(JsonNode.DeepEquals(Root,next))return;
        Remember();Root=next;
    }
}
