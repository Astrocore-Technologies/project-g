using System.Numerics;
using Content.Shared.Navigation;

namespace Content.Server.Persistence;

/// <summary>One-time relocation, retaining identity, items, skills and private progression.</summary>
public sealed class RiverLandingMigration(NavigationGrid grid, Vector2 entry)
{
    public CharacterState Apply(CharacterState state)
    {
        if (state.WorldLayoutVersion == 1) return state;
        if (state.WorldLayoutVersion != 0 || state.RegionId is not ("prototype" or "outskirts") ||
            !grid.TryFindSpawn(entry, out var spawn))
            throw new InvalidDataException("Unsupported River Landing migration source.");
        var progression = state.Progression;
        if (progression is not null)
        {
            var maps = (progression.Exploration is { } current ? new[] { current } : [])
                .Concat(progression.OtherExplorations ?? []).ToArray();
            var old = maps.SingleOrDefault(m => m.RegionKey == "prototype");
            var cells = new ulong[(grid.CellCount + 63) / 64];
            if (old is not null)
            {
                old.Validate();
                if (old.Width != 30 || old.Height != 30 || old.OriginX != -15 || old.OriginZ != -15 || old.CellSize != 1)
                    throw new InvalidDataException("Unknown legacy exploration geometry; migration refused.");
                for (var i = 0; i < old.Width * old.Height; i++)
                {
                    if ((old.Cells[i / 64] & (1UL << (i % 64))) == 0) continue;
                    var cell = grid.Cell(new(old.OriginX + i % old.Width + .5f, old.OriginZ + i / old.Width + .5f));
                    if (cell >= 0 && cell < grid.CellCount) cells[cell / 64] |= 1UL << (cell % 64);
                }
            }
            progression = progression with
            {
                Exploration = new SavedExploration { RegionKey = "prototype", Width = grid.Width, Height = grid.Height,
                    OriginX = grid.Origin.X, OriginZ = grid.Origin.Y, CellSize = grid.CellSize, Cells = cells,
                    Tutorial = old?.Tutorial ?? progression.Exploration?.Tutorial ?? 0, Places = old?.Places ?? progression.Discoveries },
                OtherExplorations = maps.Where(m => m.RegionKey == "outskirts").ToArray()
            };
            progression.Validate();
        }
        // Existing instance IDs and cooldowns survive; positions must belong to the new map.
        var echoes = state.Echoes is { } saved ? saved with
        { Active = saved.Active.Select(e => e with { X = spawn.X, Z = spawn.Y }).ToArray() } : null;
        return state with { WorldLayoutVersion = 1, RegionId = "prototype", X = spawn.X, Z = spawn.Y,
            Echoes = echoes, Progression = progression };
    }
}
