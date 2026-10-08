using Content.Shared.Network;

namespace Content.Server.Regions;

/// <summary>Shared by all regional simulations on the fixed-tick thread; IDs never recycle.</summary>
public sealed class RuntimeEntityAllocator
{
    private ulong next = 1;

    public NetworkEntityId Allocate()
    {
        if (next == 0) throw new InvalidOperationException("Runtime entity IDs exhausted.");
        return new(next++);
    }
}
