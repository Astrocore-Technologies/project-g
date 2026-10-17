using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class HitContextProtocolTests
{
    private static AttackEvent Attack=>new(new(1),1,1,Vector2.Zero,Vector2.UnitY,2,new(2),1,99,false,ExecutionId:1);
    private static AbilityHit Ability=>new(1,new(1),new(2),1,1,99);
    private static CombatJournalEntry Entry=>new(1,100000,CombatJournalKind.Damage,"Источник","Вы",1);
    private static NetDataReader Body(NetDataWriter packet)=>new(packet.CopyData(),2,packet.Length);

    [Fact]public void EveryValidCombinationSurvivesBasicAbilityAndPrivateJournalRoundTrips()
    {
        foreach(var facing in new[]{CombatHitTags.None,CombatHitTags.FrontHit,CombatHitTags.SideHit,CombatHitTags.BackHit})
        foreach(var posture in new[]{CombatHitTags.None,CombatHitTags.AirborneHit,CombatHitTags.ProneHit})
        foreach(var combo in new[]{CombatHitTags.None,CombatHitTags.ComboHit})
        {
            var tags=facing|posture|combo;var attack=Attack with {Tags=tags};var ability=Ability with {Tags=tags};
            Assert.True(NetworkProtocol.TryReadAttackEvent(Body(NetworkProtocol.Write(attack)),out var actualAttack));Assert.Equal(attack,actualAttack);
            Assert.True(NetworkProtocol.TryReadAbilityHit(Body(NetworkProtocol.Write(ability)),out var actualAbility));Assert.Equal(ability,actualAbility);
            var entry=Entry with {HitTags=tags};var chunk=new CombatJournalChunk(new(1),0,0,0,0,[entry]);
            var packet=NetworkProtocol.Write(chunk);Assert.True(packet.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
            Assert.True(NetworkProtocol.TryReadCombatJournalChunk(Body(packet),out var actual));Assert.Equal(entry,Assert.Single(actual.Entries));
        }
        Assert.Equal(91,NetworkProtocol.Write(Attack).Length);Assert.Equal(50,NetworkProtocol.Write(Ability).Length);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(Attack with {TargetId=default,Damage=0,TargetHealth=0,Tags=CombatHitTags.FrontHit}));
    }

    [Theory][InlineData(3)][InlineData(5)][InlineData(6)][InlineData(7)][InlineData(24)][InlineData(64)][InlineData(128)][InlineData(255)]
    public void UnknownOrContradictoryContextsAreRejectedBeforePublicationAndReading(byte value)
    {
        var tags=(CombatHitTags)value;
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(Attack with {Tags=tags}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(Ability with {Tags=tags}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new CombatJournalChunk(new(1),0,0,0,0,[Entry with {HitTags=tags}])));
        var attack=NetworkProtocol.Write(Attack).CopyData();attack[^2]=value;
        var ability=NetworkProtocol.Write(Ability).CopyData();ability[^1]=value;
        var journal=NetworkProtocol.Write(new CombatJournalChunk(new(1),0,0,0,0,[Entry])).CopyData();journal[^1]=value;
        Assert.False(NetworkProtocol.TryReadAttackEvent(new(attack,2,attack.Length),out _));
        Assert.False(NetworkProtocol.TryReadAbilityHit(new(ability,2,ability.Length),out _));
        Assert.False(NetworkProtocol.TryReadCombatJournalChunk(new(journal,2,journal.Length),out _));
    }
}
