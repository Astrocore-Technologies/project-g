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
            // Exercise authoring invariants in a transient fixture. Retired maps
            // are deliberately absent from the production scene/export graph.
            var root = Fixture();
            var definition = new RegionPackageExporter.Definition("export_fixture", "res://Scenes/Regions/Fixture.tscn",
                "Data/Regions/fixture.json", "Data/Regions/fixture-points.json", new(StringComparer.Ordinal)
                {
                    ["Spawn"] = Guid.Parse(root.GetNode<EntryMarker>("AuthoringAnchors/Spawn").AuthoredObjectId),
                    ["Gate"] = Guid.Parse(root.GetNode<GateMarker>("AuthoringAnchors/SouthGate").AuthoredObjectId)
                });
            var entry = RegionPackageExporter.ExportScene(root, definition);
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
                body.Position = new Vector3(8, body.Position.Y, 12);
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

    private static RegionRoot Fixture()
    {
        var root = new RegionRoot { Name = "ExportFixture", RegionId = "export_fixture", MovementBounds = new(0,0,20,20) };
        foreach (var name in new[] { "Environment", "Terrain", "Decorations", "PublicStateBindings", "AuthoringAnchors" })
            root.AddChild(new Node3D { Name = name });
        var floor = new StaticBody3D { Name = "Floor", Position = new(10,-.5f,10) };
        floor.AddToGroup("region_walkable"); root.GetNode("Terrain").AddChild(floor);
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(20,1,20) } });
        var blocker = new StaticBody3D { Name = "VisibleBlocker", Position = new(10,1,10) };
        blocker.AddToGroup("region_movement_blocker"); root.GetNode("Decorations").AddChild(blocker);
        blocker.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(2,2,2) } });
        root.GetNode("AuthoringAnchors").AddChild(new EntryMarker { Name = "Spawn", Position = new(2,0,2),
            AuthoredObjectId = "10000000-0000-4000-8000-000000000001" });
        root.GetNode("AuthoringAnchors").AddChild(new GateMarker { Name = "SouthGate", Position = new(17,0,17), Radius = 1,
            AuthoredObjectId = "10000000-0000-4000-8000-000000000002" });
        return root;
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid authored data was accepted.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
