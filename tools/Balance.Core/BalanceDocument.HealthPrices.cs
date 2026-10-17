using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;

namespace ProjectG.Balance;

public sealed partial class BalanceDocument
{
    public void ConfigureHealthPrice(string path,double cost,bool allowFatal)
    {
        if(!double.IsFinite(cost)||cost<0||!path.StartsWith("abilities/",StringComparison.Ordinal)||path.Count(c=>c=='/')!=1)
            throw new ArgumentException("Выберите активный навык и неотрицательную стоимость HP.");
        var next=(JsonObject)Root.DeepClone();
        if(Resolve(next,path) is not JsonObject skill)throw new ArgumentException("Навык не найден.");
        skill["healthCost"]=cost;skill["allowFatalHealthCost"]=cost>0&&allowFatal;
        _=ContentCatalog.Parse(Encoding.UTF8.GetBytes(next.ToJsonString()));
        if(JsonNode.DeepEquals(Root,next))return;
        Remember();Root=next;
    }
}
