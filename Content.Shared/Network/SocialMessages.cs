using System.Numerics;
namespace Content.Shared.Network;
public enum SocialKind:byte{Party=2,Guild=3}
public enum SocialAction:byte{Create,Invite,Accept,Decline,Leave,Kick,Transfer,Resize,Promote,Demote,Disband}
public enum SocialRole:byte{Member,Officer,Leader}
public enum SocialOutcome:byte{Accepted,InvalidState,NotAllowed,Stale,Full,Missing,Busy,RateLimited,InvalidOperation,InvalidName}
public readonly record struct SocialCommand(ulong Operation,SocialKind Kind,SocialAction Action,ulong Target,ulong ExpectedRevision,ulong Token,byte Capacity,bool Acknowledge,string Name);
public readonly record struct SocialResult(ulong Operation,uint Tick,SocialOutcome Outcome);
public readonly record struct SocialMember(ulong Handle,SocialRole Role,bool Online=false);
public readonly record struct SocialRoster(NetworkEntityId Owner,uint Tick,ulong Self,ulong LastOperation,SocialKind Kind,ulong Id,ulong Revision,byte Capacity,ulong Leader,string Name,byte Page,byte Total,SocialMember[] Members);
public readonly record struct SocialInvite(ulong Token,SocialKind Kind,ulong From,ulong Id,ulong Revision,string Name,bool Handoff,float Seconds);
public readonly record struct SocialInvites(NetworkEntityId Owner,uint Tick,SocialInvite[] Items);
public readonly record struct PartyMemberPresence(ulong Handle,byte Flags,float Health,float MaxHealth,Vector2 Position);
public readonly record struct PartyPresence(NetworkEntityId Owner,uint Tick,ulong Party,ulong Revision,byte Page,PartyMemberPresence[] Members);
