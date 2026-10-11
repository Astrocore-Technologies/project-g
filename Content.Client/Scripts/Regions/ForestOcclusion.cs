using Godot;

namespace ProjectG.Regions;

/// <summary>Local camera readability; caches shared forest materials once per region.</summary>
public partial class ForestOcclusion : Node
{
	private Node3D? _focus;
	private ShaderMaterial[] _materials = [];

	public void Initialize(Node3D focus, Node3D region)
	{
		_focus = focus;
		var materials = new HashSet<ShaderMaterial>();
		Collect(region);
		_materials = materials.ToArray();
		void Collect(Node node)
		{
			if (node is GeometryInstance3D geometry && geometry.MaterialOverride is ShaderMaterial material &&
				(material.ResourcePath.EndsWith("/painted_forest.tres", StringComparison.Ordinal) ||
				 material.ResourcePath.EndsWith("/painted_gardens.tres", StringComparison.Ordinal))) materials.Add(material);
			foreach (var child in node.GetChildren()) Collect(child);
		}
	}

	public override void _Process(double delta)
	{
		if (!IsInstanceValid(_focus)) return;
		foreach (var material in _materials) material.SetShaderParameter("focus_world", _focus!.GlobalPosition);
	}

	public override void _ExitTree()
	{
		foreach (var material in _materials) material.SetShaderParameter("focus_world", Vector3.One*-10000);
		_focus = null;
		_materials = [];
	}
}
