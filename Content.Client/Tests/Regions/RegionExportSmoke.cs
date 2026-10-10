using Content.Shared.Regions;
using Godot;
using ProjectG.Regions.Authoring;

namespace ProjectG.Regions.Export;

/// <summary>Exercises real scene resources and mutations without writing to authored scenes.</summary>
public partial class RegionExportSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var first = RegionPackageExporter.Build();
            var second = RegionPackageExporter.Build();
            Require(first.Encode().SequenceEqual(second.Encode()), "Repeated export changed bytes.");
            var entry = first.Regions.Single(r => r.Id == "river_city");
            var definition = new RegionPackageExporter.Definition(entry.Id, entry.ScenePath, entry.GeometryFile, entry.PlacementsFile, entry.Bindings);
            var root = GD.Load<PackedScene>(entry.ScenePath).Instantiate<RegionRoot>();
            try
            {
                var marker = root.GetNode<EntryMarker>("AuthoringAnchors/Spawn");
                marker.Name = "RenamedSpawn";
                var renamed = RegionPackageExporter.ExportScene(root, definition);
                Require(renamed.Anchors.SequenceEqual(entry.Anchors), "Rename changed stable export identity.");
                var original = marker.Position;
                marker.Position += Vector3.Right;
                var moved = RegionPackageExporter.ExportScene(root, definition);
                var id = Guid.Parse(marker.AuthoredObjectId);
                Require(moved.Anchors.Single(a => a.Id == id).X == original.X + 1, "Moved marker was ignored.");
                marker.Position = original;
                var gate = root.GetNode<GateMarker>("AuthoringAnchors/SouthGate");
                var gateId = gate.AuthoredObjectId; gate.AuthoredObjectId = marker.AuthoredObjectId;
                Reject(() => RegionPackageExporter.ExportScene(root, definition)); gate.AuthoredObjectId = gateId;
                var body = root.FindChildren("*", "StaticBody3D", true, false).Cast<StaticBody3D>()
                    .First(n => n.IsInGroup("region_movement_blocker"));
                var bodyPosition = body.Position;
                body.Position = new Vector3(0, body.Position.Y, 18);
                Require(FlatGeometryBake.Bake(root).Hash != entry.Geometry.Hash, "Moved blocker did not change navigation hash.");
                body.Position = bodyPosition;
                var collision = body.GetChildren().OfType<CollisionShape3D>().First();
                var shape = collision.Shape;
                collision.Shape = new SphereShape3D(); Reject(() => FlatGeometryBake.Bake(root)); collision.Shape = shape;
                body.AddToGroup("region_walkable"); Reject(() => FlatGeometryBake.Bake(root)); body.RemoveFromGroup("region_walkable");
            }
            finally { root.Free(); }
            Require(first.Encode().SequenceEqual(RegionPackageExporter.Build().Encode()), "Validation changed saved scene content.");
            GD.Print("REGION_EXPORT_SMOKE_OK: deterministic export, stable rename/move, blocker hash, duplicate IDs and unsupported geometry.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid authored data was accepted.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
