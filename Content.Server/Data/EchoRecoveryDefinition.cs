namespace Content.Server.Data;

/// <summary>Existing item instances pay for recovery; acquisition belongs to the content/economy systems.</summary>
public sealed record EchoRecoveryDefinition
{
    public required string SealItemId { get; init; }
    public required int Quantity { get; init; }

    internal void Validate(ContentCatalog catalog)
    {
        if (SealItemId is null || !catalog.Items.TryGetValue(SealItemId, out var item) ||
            item.UseKind != ItemUseKind.EchoRestoration || Quantity is < 1 or > Content.Shared.Network.NetworkConstants.MaxInventoryItems)
            throw new ArgumentException("echoRecovery: invalid seal item reference/quantity.");
    }
}
