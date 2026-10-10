using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class AbilityAreaTests
{
    public static TheoryData<AbilityArea> Shapes => new()
    {
        new(AbilityAreaShape.Circle, 3),
        new(AbilityAreaShape.Sector, 2, HalfAngleRadians: MathF.PI / 3),
        new(AbilityAreaShape.Sector, 2, HalfAngleRadians: MathF.PI, Stationary: true),
        new(AbilityAreaShape.Corridor, 20, .45f),
        new(AbilityAreaShape.Self, Stationary: true)
    };
    private static AbilityProfile Profile => new(20, AbilityForm.Melee, 2, .3f, 0, .2, 6, 0, 0);
    private static NetDataReader Body(NetDataWriter packet) => new(packet.CopyData(), 2, packet.Length);

    [Theory, MemberData(nameof(Shapes))]
    public void FootprintSurvivesOwnerLoadoutAndObserverEffect(AbilityArea area)
    {
        var profile = Profile with { Area = area };
        var packet = NetworkProtocol.Write(new AbilityLoadout(new(1), 1, 50, 50, [profile]));
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(packet), out var loadout));
        Assert.Equal(profile, Assert.Single(loadout.Abilities));
        var effect = new AbilityEffectState(1, new(1), 1, 1, 20, AbilityForm.Melee,
            AbilityPhase.Telegraph, Vector2.Zero, Vector2.Zero, Vector2.UnitY, 2, 0, .2f, Area: area);
        packet = NetworkProtocol.Write(effect);
        Assert.Equal(92, packet.Length);
        Assert.True(NetworkProtocol.TryReadAbilityEffectState(Body(packet), out var copy));
        Assert.Equal(effect, copy);
    }

    [Fact]
    public void FullBarPlusDashFitsSinglePacket()
    {
        var profiles = Enumerable.Range(1, NetworkConstants.MaxAbilityProfiles)
            .Select(id => Profile with { Id = (ushort)id, Area = new(AbilityAreaShape.Sector, 2, HalfAngleRadians: 1) }).ToArray();
        var packet = NetworkProtocol.Write(new AbilityLoadout(new(1), 1, 50, 50, profiles));
        Assert.Equal(733, packet.Length);
        Assert.True(packet.Length <= NetworkConstants.MaxGamePacketBytes);
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(packet), out var copy));
        Assert.Equal(profiles, copy.Abilities);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(5)] [InlineData(9)] [InlineData(13)]
    public void MalformedGeometryCannotEnterThroughEitherPacket(int field)
    {
        var area = new AbilityArea(AbilityAreaShape.Sector, 2, HalfAngleRadians: 1);
        var owner = NetworkProtocol.Write(new AbilityLoadout(new(1), 1, 50, 50, [Profile with { Area = area }]));
        var observer = NetworkProtocol.Write(new AbilityEffectState(1, new(1), 1, 1, 20, AbilityForm.Melee,
            AbilityPhase.Telegraph, Vector2.Zero, Vector2.Zero, Vector2.UnitY, 2, 0, .2f, Area: area));
        foreach (var packet in new[] { owner, observer })
        {
            var bytes = packet.CopyData(); var offset = bytes.Length - AbilityArea.WireBytes - (packet == owner ? sizeof(double) : 0) + field;
            if (field is 0 or 13) bytes[offset] = 255;
            else BitConverter.GetBytes(float.NaN).CopyTo(bytes, offset);
            var reader = new NetDataReader(bytes, 2, bytes.Length);
            Assert.False(packet == owner ? NetworkProtocol.TryReadAbilityLoadout(reader, out _) : NetworkProtocol.TryReadAbilityEffectState(reader, out _));
            for (var length = bytes.Length - AbilityArea.WireBytes; length < bytes.Length; length++)
            {
                reader = new(packet.CopyData(), 2, length);
                Assert.False(packet == owner ? NetworkProtocol.TryReadAbilityLoadout(reader, out _) : NetworkProtocol.TryReadAbilityEffectState(reader, out _));
            }
        }
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new AbilityLoadout(new(1), 1, 50, 50,
            [Profile with { Area = area with { HalfAngleRadians = 0 } }])));
    }

    [Fact]
    public void FootprintsSpendRangeOnHeightAndRejectOutsideEdges()
    {
        var circle = new AbilityArea(AbilityAreaShape.Circle, 10);
        Assert.True(circle.Contains(new(6, 8, 0), Vector2.UnitY));
        Assert.False(circle.Contains(new(8, 8, 0), Vector2.UnitY));
        var corridor = new AbilityArea(AbilityAreaShape.Corridor, 3, .5f);
        Assert.True(corridor.Contains(new(.5f, 1, 2), Vector2.UnitY));
        Assert.False(corridor.Contains(new(.51f, 0, 2), Vector2.UnitY));
        Assert.False(corridor.Contains(new(0, 0, -1), Vector2.UnitY));
        Assert.False(corridor.Contains(new(0, 3, 1), Vector2.UnitY));
        var sector = new AbilityArea(AbilityAreaShape.Sector, 3, HalfAngleRadians: MathF.PI / 4);
        Assert.True(sector.Contains(new(1, 1, 2), Vector2.UnitY));
        Assert.False(sector.Contains(new(2, 0, 1), Vector2.UnitY));
        Assert.False(sector.Contains(new(0, 0, -1), Vector2.UnitY));
        Assert.True((sector with { HalfAngleRadians = MathF.PI }).Contains(new(0, 0, -1), Vector2.UnitY));
    }
}
