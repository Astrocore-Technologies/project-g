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
        for (var start = 0; start < message.BlockedCells.Length; start += 8)
        {
            byte bits = 0;
            for (var bit = 0; bit < 8 && start + bit < message.BlockedCells.Length; bit++)
                bits |= (byte)(message.BlockedCells[start + bit] << bit);
            writer.Put(bits);
        }
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
        if (count == 0 || count > NetworkConstants.MaxNavigationCells || reader.AvailableBytes != (count + 7) / 8)
            return false;
        var blocked = new byte[(int) count];
        for (var start = 0; start < blocked.Length; start += 8)
        {
            if (!reader.TryGetByte(out var bits)) return false;
            var remaining = Math.Min(8, blocked.Length - start);
            if (remaining < 8 && (bits >> remaining) != 0) return false;
            for (var bit = 0; bit < remaining; bit++)
                blocked[start + bit] = (byte)((bits >> bit) & 1);
        }
        var candidate = new RegionNavigation(origin, cellSize, agentRadius, width, height, blocked);
        if (!NavigationGrid.IsValid(candidate))
            return false;
        message = candidate;
        return true;
    }
}
