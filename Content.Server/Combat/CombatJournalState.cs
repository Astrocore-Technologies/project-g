using Content.Server.Persistence;
using Content.Shared.Combat;
namespace Content.Server.Combat;

/// <summary>D048/D049: target-owned bounded evidence; no world scan or per-idle-player timer.</summary>
internal sealed class CombatJournalState
{
    private readonly Queue<CombatJournalEntry> _recent=new(300);
    private readonly List<SavedDeathLog> _deaths=new(10);
    private ulong _serial;
    internal IReadOnlyList<SavedDeathLog> Deaths=>_deaths;
    internal ulong LastId=>_serial;
    internal CombatJournalEntry Record(long at,CombatJournalKind kind,string source,string target,double amount=0,
        double duration=0,double strength=0,ushort skill=0,CrowdControlKind control=default,CombatJournalFlags flags=0)
    {
        if(_serial==ulong.MaxValue)throw new InvalidOperationException("Combat journal ordinal exhausted.");
        // A server wall-clock correction cannot reverse recorded evidence or expand its death window.
        if(_recent.Count>0)at=Math.Max(at,_lastTime);
        var entry=new CombatJournalEntry(++_serial,at,kind,source,target,amount,duration,strength,skill,control,flags);
        if(!entry.IsValid)throw new InvalidOperationException("Invalid combat journal observation.");
        if(_recent.Count==300)_recent.Dequeue();_recent.Enqueue(entry);_lastTime=at;return entry;
    }
    private long _lastTime;
    internal CombatJournalEntry[] Current()=>_recent.Skip(Math.Max(0,_recent.Count-200)).ToArray();
    internal CombatJournalEntry[] Session()=>_recent.ToArray();
    internal void Died(ulong death)
    {
        var entries=_recent.Where(e=>_lastTime-e.AtUnixMilliseconds<=30000).ToArray();
        var log=new SavedDeathLog(1,death,_lastTime,entries);log.Validate();
        if(_deaths.Count==10)_deaths.RemoveAt(0);_deaths.Add(log);
    }
    internal void Restore(IReadOnlyList<SavedDeathLog>? deaths,IReadOnlyList<CombatJournalEntry>? session=null)
    {
        if(_recent.Count!=0 || _deaths.Count!=0 || _serial!=0)throw new InvalidOperationException("Journal can only be restored into a fresh actor.");
        if(deaths is not null)
        {
            if(deaths.Count>10)throw new InvalidDataException("Death log count exceeds D049.");
            ulong previous=0;
            foreach(var death in deaths)
            {death.Validate();if(death.DeathId<=previous)throw new InvalidDataException("Duplicate or unordered death logs.");previous=death.DeathId;_deaths.Add(death with {Entries=[..death.Entries]});}
        }
        if(session is null)return;
        if(session.Count>300)throw new InvalidDataException("Session journal exceeds D049.");
        foreach(var entry in session)
        {
            if(!entry.IsValid || entry.Id<=_serial || entry.AtUnixMilliseconds<_lastTime)throw new InvalidDataException("Invalid session journal order.");
            _recent.Enqueue(entry);_serial=entry.Id;_lastTime=entry.AtUnixMilliseconds;
        }
    }
}
