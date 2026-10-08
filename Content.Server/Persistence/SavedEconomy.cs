using System.Text.Json.Serialization;
using Content.Shared.Network;
namespace Content.Server.Persistence;
public sealed record SavedEconomy
{
    [JsonRequired] public int Version {get;init;}=1;
    public required ulong LastOperation {get;init;}
    public required int Coins {get;init;}
    public string? PayloadHash {get;init;}
    public EconomyEffect Effect {get;init;}
    public static SavedEconomy Empty=>new(){LastOperation=0,Coins=0};
    public void Validate()
    {if(Version!=1 || LastOperation>long.MaxValue || Coins is <0 or >1000000 || !Enum.IsDefined(Effect) || (LastOperation==0 ? PayloadHash is not null || Effect!=EconomyEffect.None : PayloadHash is not {Length:64} || PayloadHash.Any(c=>!char.IsAsciiHexDigit(c))))throw new InvalidDataException("Invalid economy receipt/wallet.");}
}
public sealed record SavedListing(ulong Id,Guid Seller,SavedItem Item,int Price)
{
    public void Validate(){new SavedInventory{Items=[Item]}.Validate();if(Id is 0 or >long.MaxValue || Seller==Guid.Empty || Item.EquippedSlot!=EquipmentSlot.None || Price is <1 or >1000000)throw new InvalidDataException("Invalid listing.");}
}
public sealed record SavedMarketCredit(Guid Seller,int Coins);
public sealed record SavedMarket
{
    [JsonRequired] public int Version {get;init;}=1;
    public required ulong NextListing {get;init;}
    public required SavedListing[] Listings {get;init;}
    public required SavedMarketCredit[] Credits {get;init;}
    public static SavedMarket Empty=>new(){NextListing=1,Listings=[],Credits=[]};
    public void Validate()
    {
        if(Version!=1 || NextListing is 0 or >long.MaxValue || Listings is not {Length:<=8} || Credits is not {Length:<=16})throw new InvalidDataException("Invalid market budget.");
        var ids=new HashSet<ulong>();var instances=new HashSet<Guid>();var sellers=new HashSet<Guid>();
        foreach(var l in Listings){if(l is null)throw new InvalidDataException("Null listing.");l.Validate();if(l.Id>=NextListing || !ids.Add(l.Id) || !instances.Add(l.Item.InstanceId))throw new InvalidDataException("Duplicate listing/instance.");}
        foreach(var c in Credits)if(c is null || c.Seller==Guid.Empty || !sellers.Add(c.Seller) || c.Coins is <1 or >1000000)throw new InvalidDataException("Invalid credit.");
    }
}
