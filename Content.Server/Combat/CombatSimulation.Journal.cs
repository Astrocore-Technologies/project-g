using Content.Server.Persistence;
using Content.Shared.Combat;
using Content.Shared.Network;
namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal Func<NetworkEntityId,NetworkEntityId,bool>? JournalVisibility {get;set;}
    internal IReadOnlyList<SavedDeathLog> DeathLogs(NetworkEntityId owner)=>_actors[owner].Journal?.Deaths??[];
    internal CombatJournalEntry[] CurrentJournal(NetworkEntityId owner)=>_actors[owner].Journal?.Current()??[];
    internal ulong JournalLastId(NetworkEntityId owner)=>_actors[owner].Journal?.LastId??0;
    internal CombatJournalEntry[] SessionJournal(NetworkEntityId owner)=>_actors[owner].Journal?.Session()??[];
    internal void RestoreJournal(NetworkEntityId owner,IReadOnlyList<SavedDeathLog>? deaths,IReadOnlyList<CombatJournalEntry>? session=null)
        =>_actors[owner].Journal?.Restore(deaths,session);
    private string JournalLabel(NetworkEntityId observer,NetworkEntityId subject,bool unknown=false)
    {
        if(unknown)return "Неизвестный источник";
        if(!subject.IsValid)return "Окружение";
        if(subject==observer)return "Вы";
        return _actors.TryGetValue(subject,out var actor) && JournalVisibility?.Invoke(observer,subject)!=false ? actor.PublicJournalLabel : "Неизвестный источник";
    }
    internal void RecordJournal(NetworkEntityId source,NetworkEntityId target,CombatJournalKind kind,double amount=0,
        double duration=0,double strength=0,ushort skill=0,CrowdControlKind control=default,CombatJournalFlags flags=0,bool unknownSource=false)
    {
        var at=(long)Math.Clamp(CombatClock()*1000,0,253402300799999);
        Append(source);if(target!=source)Append(target);
        void Append(NetworkEntityId owner)
        {
            if(!_actors.TryGetValue(owner,out var actor) || actor.Journal is not {} journal)return;
            var revealed=source==owner || !source.IsValid || JournalVisibility?.Invoke(owner,source)!=false;
            journal.Record(at,kind,JournalLabel(owner,source,unknownSource),JournalLabel(owner,target),amount,duration,strength,
                revealed&&!unknownSource?skill:(ushort)0,control,flags);
        }
    }
}
