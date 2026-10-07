using Content.Shared.Navigation;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(RegionNavigation message)
    {
        if (!NavigationGrid.IsValid(message))
            throw new ArgumentException("Invalid navigation message.", nameof(message));
        var writer = CreateWriter(NetworkMessageType.RegionNavigation);
        WriteVector2(writer, message.Origin);
        writer.Put(message.CellSize);
        writer.Put(message.AgentRadius);
        writer.Put(message.Width);
        writer.Put(message.Height);
        foreach (var cell in message.BlockedCells)
            writer.Put(cell);
        return writer;
    }

    public static bool TryReadRegionNavigation(NetDataReader reader, out RegionNavigation message)
    {
        message = default;
        if (!TryReadVector2(reader, out var origin) ||
            !reader.TryGetFloat(out var cellSize) || !reader.TryGetFloat(out var agentRadius) ||
            !reader.TryGetUShort(out var width) || !reader.TryGetUShort(out var height))
            return false;
        var count = (long) width * height;
        // Validate the exact bounded body before allocating or reading the cell mask.
        if (count == 0 || count > NetworkConstants.MaxNavigationCells || reader.AvailableBytes != count)
            return false;
        var blocked = new byte[(int) count];
        for (var i = 0; i < blocked.Length; i++)
        {
            if (!reader.TryGetByte(out blocked[i]) || blocked[i] > 1)
                return false;
        }
        var candidate = new RegionNavigation(origin, cellSize, agentRadius, width, height, blocked);
        if (!NavigationGrid.IsValid(candidate))
            return false;
        message = candidate;
        return true;
    }
}
