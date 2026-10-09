using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared;

public sealed class QuestProtocolTests
{
    private delegate bool Read(NetDataReader reader);
    private static NetDataReader Body(byte[] bytes)
    {
        var reader = new NetDataReader(bytes);
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        return reader;
    }
    private static void Check(NetDataWriter writer, Read read)
    {
        var bytes = writer.CopyData(); Assert.True(read(Body(bytes)));
        for (var length = 0; length < bytes.Length; length++)
        {
            var reader = new NetDataReader(bytes[..length]);
            Assert.False(NetworkProtocol.TryReadMessageType(reader, out _) && read(reader));
        }
        Assert.False(read(Body([.. bytes, 0])));
    }
    [Fact]
    public void EveryMessageRoundTripsAndRejectsAllTruncationsAndTrailingBytes()
    {
        var command = new QuestCommand(1, QuestAction.Accept, new(3));
        Check(NetworkProtocol.Write(command), r => NetworkProtocol.TryReadQuestCommand(r, out var actual) && actual == command);
        var npc = new QuestNpcSpawn(new(3), 50, new(-15, 18), "Лада", "Снабженец пристани");
        Check(NetworkProtocol.Write(npc), r => NetworkProtocol.TryReadQuestNpcSpawn(r, out var actual) && actual == npc);
        var reply = new QuestReply(1, 50, new(3), QuestOutcome.Accepted, 1, "Лада", "Нужна помощь с доставкой.");
        Check(NetworkProtocol.Write(reply), r => NetworkProtocol.TryReadQuestReply(r, out var actual) && actual == reply);
        var journal = new QuestJournal(new(2), 51, QuestStatus.Active, "Доставка", "Отнести древесину смотрителю.", "Древесина", 3, 3, 20);
        Check(NetworkProtocol.Write(journal), r => NetworkProtocol.TryReadQuestJournal(r, out var actual) && actual == journal);
        Check(NetworkProtocol.Write(new QuestCommand(2, QuestAction.Journal, default)), r => NetworkProtocol.TryReadQuestCommand(r, out _));
    }
    [Fact]
    public void InvalidIdsEnumsNumbersStringsAndOversizedMessagesFailClosed()
    {
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new QuestCommand(0, QuestAction.Talk, new(1))));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new QuestCommand(1, (QuestAction)255, new(1))));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new QuestCommand(1, QuestAction.Journal, new(1))));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new QuestCommand(1, QuestAction.Talk, default)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new QuestNpcSpawn(new(1), 0, new(float.NaN, 0), "NPC", "Role")));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new QuestNpcSpawn(default, 0, new(0, 0), "NPC", "Role")));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new QuestReply(1, 0, new(1), QuestOutcome.Accepted, 16, "NPC", "text")));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new QuestReply(1, 0, new(1), (QuestOutcome)255, 0, "NPC", "text")));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new QuestReply(1, 0, new(1), QuestOutcome.Accepted, 0, "NPC", new string('x', 201))));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new QuestJournal(new(1), 0, QuestStatus.Unknown, "Title", "Objective", "Wood", 3, 0, 20)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new QuestJournal(new(1), 0, QuestStatus.Active, "Title", "Objective", "Wood", 3, 1000, 20)));
        Assert.False(NetworkProtocol.TryReadQuestNpcSpawn(new(new byte[217]), out _));
        Assert.False(NetworkProtocol.TryReadQuestReply(new(new byte[719]), out _));
        Assert.False(NetworkProtocol.TryReadQuestJournal(new(new byte[784]), out _));
        var bytes = NetworkProtocol.Write(new QuestCommand(1, QuestAction.Talk, new(2))).CopyData();
        bytes[6] = 255; Assert.False(NetworkProtocol.TryReadQuestCommand(Body(bytes), out _));
    }
}
