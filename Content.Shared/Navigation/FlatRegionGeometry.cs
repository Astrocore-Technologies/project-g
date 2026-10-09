using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared.Network;
using LiteNetLib.Utils;

namespace Content.Shared.Navigation;

/// <summary>Offline scene export. Only public collision geometry, never gameplay rules.</summary>
public sealed record FlatRegionGeometry(int Version, byte[] Navigation)
{
    public int[] OpeningCells { get; init; } = [];
    public NavigationGrid CreateGrid()
    {
        if (Version != 1 || Navigation is null || Navigation.Length > NetworkConstants.MaxGamePacketBytes)
            throw new InvalidDataException("Unsupported region geometry export.");
        var reader = new NetDataReader(Navigation);
        if (!NetworkProtocol.TryReadMessageType(reader, out var type) || type != NetworkMessageType.RegionNavigation ||
            !NetworkProtocol.TryReadRegionNavigation(reader, out var map))
            throw new InvalidDataException("Invalid region geometry export.");
        if (OpeningCells is null || OpeningCells.Length > 16 || OpeningCells.Distinct().Count() != OpeningCells.Length ||
            OpeningCells.Any(i => i < 0 || i >= map.BlockedCells.Length || map.BlockedCells[i] != 1))
            throw new InvalidDataException("Invalid authored opening patch.");
        return new NavigationGrid(map);
    }

    [JsonIgnore] public ulong Hash
    {
        get
        {
            var bytes = new byte[Navigation.Length + OpeningCells.Length * sizeof(int)];
            Navigation.CopyTo(bytes, 0);
            for (var i = 0; i < OpeningCells.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(Navigation.Length + i * 4), OpeningCells[i]);
            return BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(bytes));
        }
    }
    public static FlatRegionGeometry FromGrid(NavigationGrid grid) => new(1, NetworkProtocol.Write(grid.ToMessage()).CopyData());
    public static FlatRegionGeometry Parse(string json)
    {
        if (json.Length > 4096) throw new InvalidDataException("Region export exceeds budget.");
        var result = JsonSerializer.Deserialize<FlatRegionGeometry>(json) ?? throw new InvalidDataException("Missing region export.");
        result.CreateGrid();
        return result;
    }
}
