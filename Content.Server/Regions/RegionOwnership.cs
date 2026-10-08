namespace Content.Server.Regions;

public enum RegionTransferPhase : byte { Prepared, Committed, Completed, Cancelled, RecoveryRequired }

/// <summary>An epoch fences delayed commands; the database lease remains process-owned.</summary>
public readonly record struct RegionOwner(string Region, ulong Epoch);

/// <summary>
/// Single-threaded ownership coordinator. Reservations are not simulated actors.
/// Persistence runs outside this class; only a confirmed commit changes the owner.
/// </summary>
public sealed class RegionOwnership
{
    private readonly Dictionary<Guid, RegionOwner> owners = new();
    private readonly Dictionary<Guid, RegionTransfer> pending = new();
    private readonly int capacity;
    private ulong nextEpoch = 1;

    public RegionOwnership(int capacity = 64)
    {
        if (capacity is < 1 or > 400) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    public RegionOwner Owner(Guid character) => owners.TryGetValue(character, out var owner)
        ? owner : throw new InvalidOperationException("Character has no region owner.");

    public void Register(Guid character, string region)
    {
        ValidateRegion(region);
        if (character == Guid.Empty || owners.Count >= capacity || owners.ContainsKey(character))
            throw new InvalidOperationException("Invalid, duplicate or over-budget region ownership.");
        owners.Add(character, new(region, AllocateEpoch()));
    }

    public bool CanExecute(Guid character, RegionOwner expected) =>
        owners.TryGetValue(character, out var owner) && owner == expected && !pending.ContainsKey(character);

    public RegionTransfer Prepare(Guid character, Guid operation, RegionOwner expected, string destination)
    {
        ValidateRegion(destination);
        if (operation == Guid.Empty || !CanExecute(character, expected) ||
            expected.Region == destination)
            throw new InvalidOperationException("Invalid or stale region transfer.");
        var transfer = new RegionTransfer(character, operation, expected, new(destination, AllocateEpoch()));
        pending.Add(character, transfer);
        return transfer;
    }

    public void ConfirmCommit(RegionTransfer transfer)
    {
        RequirePending(transfer, RegionTransferPhase.Prepared);
        // This is called only after durable storage confirms the destination state.
        owners[transfer.Character] = transfer.Destination;
        transfer.CommitConfirmed = true;
        transfer.Phase = RegionTransferPhase.Committed;
    }

    public void Complete(RegionTransfer transfer)
    {
        RequirePending(transfer, RegionTransferPhase.Committed);
        transfer.Phase = RegionTransferPhase.Completed;
        pending.Remove(transfer.Character);
    }

    public void CancelBeforeCommit(RegionTransfer transfer)
    {
        RequirePending(transfer, RegionTransferPhase.Prepared);
        transfer.Phase = RegionTransferPhase.Cancelled;
        pending.Remove(transfer.Character);
    }

    public void RequireRecovery(RegionTransfer transfer)
    {
        if (!pending.TryGetValue(transfer.Character, out var current) || !ReferenceEquals(current, transfer) ||
            transfer.Phase is not (RegionTransferPhase.Prepared or RegionTransferPhase.Committed))
            throw new InvalidOperationException("Transfer is not recoverable.");
        // A timeout is not proof of rollback. Keep both regions unable to execute.
        transfer.Phase = RegionTransferPhase.RecoveryRequired;
    }

    public void ResolveRecovery(RegionTransfer transfer, bool destinationCommitted)
    {
        RequirePending(transfer, RegionTransferPhase.RecoveryRequired);
        if (transfer.CommitConfirmed && !destinationCommitted)
            throw new InvalidOperationException("A confirmed destination commit cannot be rolled back to the source.");
        owners[transfer.Character] = destinationCommitted ? transfer.Destination : transfer.Source;
        transfer.CommitConfirmed = destinationCommitted;
        transfer.Phase = destinationCommitted ? RegionTransferPhase.Committed : RegionTransferPhase.Prepared;
    }

    public void Unregister(Guid character)
    {
        if (pending.ContainsKey(character)) throw new InvalidOperationException("Resolve transfer before releasing ownership.");
        owners.Remove(character);
    }

    private void RequirePending(RegionTransfer transfer, RegionTransferPhase phase)
    {
        if (transfer.Phase != phase || !pending.TryGetValue(transfer.Character, out var current) ||
            !ReferenceEquals(current, transfer)) throw new InvalidOperationException("Stale transfer phase or token.");
    }

    private ulong AllocateEpoch()
    {
        if (nextEpoch == 0) throw new InvalidOperationException("Region ownership epochs exhausted.");
        return nextEpoch++;
    }

    internal static void ValidateRegion(string region)
    {
        if (string.IsNullOrEmpty(region) || region.Length > 64)
            throw new ArgumentException("Invalid region key.", nameof(region));
        foreach (var c in region)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
                throw new ArgumentException("Region keys use lowercase ASCII letters, digits and underscore.", nameof(region));
    }
}

public sealed class RegionTransfer
{
    internal RegionTransfer(Guid character, Guid operation, RegionOwner source, RegionOwner destination)
    { Character = character; Operation = operation; Source = source; Destination = destination; }
    public Guid Character { get; }
    public Guid Operation { get; }
    public RegionOwner Source { get; }
    public RegionOwner Destination { get; }
    public RegionTransferPhase Phase { get; internal set; } = RegionTransferPhase.Prepared;
    internal bool CommitConfirmed { get; set; }
}
