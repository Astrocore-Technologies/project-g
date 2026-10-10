using System.Text.Json.Serialization;
using Content.Shared.Network;
namespace Content.Server.Persistence;
public sealed record SavedAggression(Guid Victim,long Until,bool Civilian);
public sealed record SavedPvp
{
    [JsonRequired] public int Version {get;init;}=1;
    public PvpMode Mode {get;init;}
    public long CombatUntil {get;init;}
    public long AggressorUntil {get;init;}
    public long ProtectionUntil {get;init;}
    public int Reputation {get;init;}
    public int Pk {get;init;}
    public Guid LastAttacker {get;init;}
    public long LastPvpAt {get;init;}
    public SavedAggression[] Episodes {get;init;}=[];
    public ulong LastSequence {get;init;}
    public byte LastAction {get;init;}
    public PvpOutcome LastOutcome {get;init;}
    public PvpMode LastMode {get;init;}
    public ulong LastDeathRequest {get;init;}
    public ulong DeathId {get;init;}
    public bool Dead {get;init;}
    public bool PvpDeath {get;init;}
    public long RespawnAt {get;init;}
    public int ExperienceLost {get;init;}
    public Guid DroppedItem {get;init;}
    public bool DropSkippedCapacity {get;init;}
    public void Validate()
    {
        if(Version!=1 || !Enum.IsDefined(Mode)||!Enum.IsDefined(LastMode)||!Enum.IsDefined(LastOutcome)||LastAction>2||Reputation is <-1000 or >1000 || Pk<0 || DeathId>long.MaxValue || LastSequence>long.MaxValue || LastDeathRequest>long.MaxValue || ExperienceLost is <0 or >1000000000 || !Time(CombatUntil)||!Time(AggressorUntil)||!Time(ProtectionUntil)||!Time(LastPvpAt)||!Time(RespawnAt) || Dead&&(DeathId==0||RespawnAt==0) || Episodes is null || Episodes.Length>16)throw new InvalidDataException("Invalid PvP/death state.");
        var ids=new HashSet<Guid>();foreach(var e in Episodes)if(e is null||e.Victim==Guid.Empty||!ids.Add(e.Victim)||!Time(e.Until)||e.Until==0)throw new InvalidDataException("Invalid aggression history.");
        if((LastAttacker==Guid.Empty)!=(LastPvpAt==0))throw new InvalidDataException("Invalid damage attribution.");
    }
    private static bool Time(long n)=>n is >=0 and <=253402300799000L;
}
public sealed record SavedDeathLoot(SavedItem Item,float X,float Z,long Expires, SavedSurface? Surface = null)
{
    public void Validate(){Surface?.Validate();new SavedInventory{Items=[Item]}.Validate();if(Item.EquippedSlot!=EquipmentSlot.None||Item.Bound||!float.IsFinite(X)||!float.IsFinite(Z)||Expires is <=0 or >253402300799000L)throw new InvalidDataException("Invalid death loot.");}
}
