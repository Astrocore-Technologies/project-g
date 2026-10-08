using System.Security.Cryptography;
using System.Text;
using Content.Shared.Network;
using LiteNetLib;
namespace Content.Server.Networking;
public sealed partial class GameServerService
{
    private readonly Dictionary<int,string> _sessionCredentialHashes=new();
    private readonly Dictionary<string,(int Connection,long Until)> _detached=new();
    private readonly Dictionary<int,(NetPeer Peer,ClientHello Hello,PlayerId Player,string Key)> _combatResumes=new();
    private int _nextDetached=-1;
    internal int DetachedCombatantCount=>_detached.Count;
    private static string CredentialHash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private bool DetachCombatPlayer(int connection)
    {
        var world = WorldFor(connection);
        world.DisconnectPvp(connection);
        if(!world.IsCombatTagged(connection)||!_sessionCredentialHashes.Remove(connection,out var key))return false;
        if(_nextDetached==int.MinValue)throw new InvalidOperationException("Detached handles exhausted.");
        var detached=_nextDetached--;var session=_sessions[connection];_sessions.Remove(connection);_sessions[detached]=session;_sessionCredentialHashes[detached]=key;
        RebindRegionalPlayer(connection,detached,world.GetPlayer(connection).PlayerId);
        _detached[key]=(detached,world.PvpNow+30000);return true;
    }
    private bool TryResumeCombat(NetPeer peer,ClientHello hello,PlayerId player)
    {
        if(hello.DevelopmentToken.Length==0)return false;var key=CredentialHash(hello.DevelopmentToken);
        if(!_detached.ContainsKey(key))return false;
        _combatResumes[peer.Id]=(peer,hello,player,key);return true;
    }
    private void CompleteCombatResumes()
    {
        foreach(var (connection,r) in _combatResumes.ToArray())
        {
            _combatResumes.Remove(connection);if(!_peers.TryGetValue(connection,out var peer)||!ReferenceEquals(peer,r.Peer))continue;
            if(_detached.TryGetValue(r.Key,out var expired)&&WorldFor(expired.Connection).PvpNow>=expired.Until)
            { _detached.Remove(r.Key); _departed.Add(expired.Connection); Reject(peer,HandshakeRejectCode.CharacterInUse,"Combat session closing; retry login."); continue; }
            if(!_detached.Remove(r.Key,out var old)||!_sessions.Remove(old.Connection,out var session)){Reject(peer,HandshakeRejectCode.CharacterInUse,"Combat session closed; retry login.");continue;}
            _departed.Remove(old.Connection);_sessionCredentialHashes.Remove(old.Connection);_sessions[connection]=session;_sessionCredentialHashes[connection]=r.Key;
            RebindRegionalPlayer(old.Connection,connection,r.Player);AcceptPlayer(peer,r.Hello,r.Player,session,reattached:true);
        }
    }
    private void ExpireDetachedCombatants()
    {
        var now=_world.PvpNow;foreach(var (key,d) in _detached.ToArray())if(now>=d.Until||!WorldFor(d.Connection).IsCombatTagged(d.Connection)){_detached.Remove(key);_departed.Add(d.Connection);}
    }
}
