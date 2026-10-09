using System.Numerics;
using Content.Server.Data;
using Content.Shared.Network;
namespace Content.Server.Quests;

public sealed record QuestNpcDefinition(string Name, string Role, float X, float Z)
{ public Vector2 Position => new(X, Z); }
/// <summary>One authored delivery slice, not a permanent contract/rank/repeatability rules engine.</summary>
public sealed record DeliveryQuestDefinition
{
    public required string Id { get; init; }
    public required string RegionId { get; init; }
    public required QuestNpcDefinition Giver { get; init; }
    public required QuestNpcDefinition Recipient { get; init; }
    public required string Title { get; init; }
    public required string Objective { get; init; }
    public required string Offer { get; init; }
    public required string Reminder { get; init; }
    public required string RecipientGreeting { get; init; }
    public required string RecipientReminder { get; init; }
    public required string Thanks { get; init; }
    public required ushort MaterialId { get; init; }
    public required ushort Quantity { get; init; }
    public required int Experience { get; init; }
    public float InteractionRange { get; init; } = 2.5f;
    public void Validate(ContentCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64 || string.IsNullOrWhiteSpace(RegionId) || RegionId.Length > 64 ||
           Giver is null || Recipient is null || Giver == Recipient || !float.IsFinite(InteractionRange) || InteractionRange is <= 0 or > 4 ||
           Quantity is 0 or > 999 || Experience is < 1 or > 100000 || catalog.Crafting is null || !catalog.Crafting.Materials.Any(m => m.Id == MaterialId))
            throw new ArgumentException("Invalid delivery quest/reference.");
        // The same public bounds are checked at load and on the wire, before any login.
        foreach (var npc in new[] { Giver, Recipient }) NetworkProtocol.Write(new QuestNpcSpawn(new(1), 0, npc.Position, npc.Name, npc.Role));
        NetworkProtocol.Write(new QuestJournal(new(1), 0, QuestStatus.Active, Title, Objective, catalog.Crafting.Materials.Single(m => m.Id == MaterialId).Name, Quantity, 0, Experience));
        foreach (var line in new[] { Offer, Reminder, RecipientGreeting, RecipientReminder, Thanks }) NetworkProtocol.Write(new QuestReply(1, 0, new(1), QuestOutcome.Accepted, 0, Giver.Name, line));
    }
}
