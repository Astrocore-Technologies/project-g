using Content.Shared.Navigation;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public readonly record struct SurfaceChunk(ulong Hash, int TotalBytes, int Offset, byte[] Data);
public readonly record struct RegionLoadState(NetworkMessageType Type, ulong GeometryHash);

public static partial class NetworkProtocol
{
    public const int SurfaceChunkBytes = 900;
    private static bool ReadHeight(NetDataReader reader, out float height) =>
        reader.TryGetFloat(out height) && float.IsFinite(height) && MathF.Abs(height) <= 128;
    private static void WriteHeight(NetDataWriter writer, float height)
    {
        if (!float.IsFinite(height) || MathF.Abs(height) > 128) throw new ArgumentException("Invalid height.");
        writer.Put(height);
    }

    public static NetDataWriter Write(SurfaceChunk chunk)
    {
        if (!ValidChunk(chunk)) throw new ArgumentException("Invalid bounded surface chunk.");
        var writer = CreateWriter(NetworkMessageType.SurfaceChunk);
        writer.Put(chunk.Hash); writer.Put(chunk.TotalBytes); writer.Put(chunk.Offset); writer.PutBytesWithLength(chunk.Data);
        return writer;
    }
    private static bool ValidChunk(SurfaceChunk c) => c.Hash != 0 && c.TotalBytes is > 0 and <= SurfaceGeometry.MaxBytes &&
        c.Offset >= 0 && c.Offset % SurfaceChunkBytes == 0 && c.Data is { Length: > 0 and <= SurfaceChunkBytes } &&
        c.Offset < c.TotalBytes && c.Data.Length == Math.Min(SurfaceChunkBytes, c.TotalBytes - c.Offset);
    public static bool TryReadSurfaceChunk(NetDataReader reader, out SurfaceChunk chunk)
    {
        chunk = default;
        if (!reader.TryGetULong(out var hash) || !reader.TryGetInt(out var total) || !reader.TryGetInt(out var offset) ||
            !reader.TryGetUShort(out var length) || length > SurfaceChunkBytes || reader.AvailableBytes != length) return false;
        var bytes = new byte[length]; reader.GetBytes(bytes, length);
        chunk = new(hash, total, offset, bytes); return ValidChunk(chunk);
    }
    public static NetDataWriter Write(RegionLoadState state)
    {
        if (!LoadType(state.Type)) throw new ArgumentException("Invalid load control.");
        var writer = CreateWriter(state.Type); writer.Put(state.GeometryHash); return writer;
    }
    private static bool LoadType(NetworkMessageType type) => type is NetworkMessageType.RegionReady or
        NetworkMessageType.RegionApplied or NetworkMessageType.RegionActivated or NetworkMessageType.RegionBaseline;
    public static bool TryReadRegionLoadState(NetDataReader reader, NetworkMessageType type, out RegionLoadState state)
    {
        state = default;
        if (!LoadType(type) || !reader.TryGetULong(out var hash) || reader.AvailableBytes != 0) return false;
        state = new(type, hash); return true;
    }
}

/// <summary>One bounded, ordered assembly per epoch; duplicate/conflicting chunks are rejected.</summary>
public sealed class SurfaceAssembly
{
    private byte[]? bytes;
    private int received;
    private ulong hash;
    public SurfaceGeometry? Add(SurfaceChunk chunk)
    {
        if (chunk.TotalBytes is <= 0 or > SurfaceGeometry.MaxBytes || chunk.Data is null ||
            chunk.Data.Length != Math.Min(NetworkProtocol.SurfaceChunkBytes, chunk.TotalBytes-chunk.Offset) || chunk.Data.Length <= 0 ||
            chunk.Offset < 0 || chunk.Offset % NetworkProtocol.SurfaceChunkBytes != 0 || chunk.Hash == 0)
            throw new InvalidDataException("Invalid bounded surface assembly.");
        if (bytes is null) { bytes = new byte[chunk.TotalBytes]; hash = chunk.Hash; }
        if (hash != chunk.Hash || chunk.TotalBytes != bytes.Length || chunk.Offset != received ||
            chunk.Data.Length > bytes.Length - received) throw new InvalidDataException("Out-of-order surface export.");
        chunk.Data.CopyTo(bytes, received); received += chunk.Data.Length;
        if (received != bytes.Length) return null;
        var result = SurfaceGeometry.Decode(bytes);
        if (result.Hash != hash) throw new InvalidDataException("Surface revision mismatch.");
        return result;
    }
}
