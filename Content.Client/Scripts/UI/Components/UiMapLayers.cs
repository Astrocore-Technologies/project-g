using Godot;
namespace ProjectG.UI;

/// <summary>Only public region scenery is visible to map cameras; actors and world labels stay on gameplay layers.</summary>
public static class UiMapLayers
{
    public const uint Scenery=1u<<19;
    public static void IncludeScenery(Node root)
    {
        if(root is VisualInstance3D visual && root is not Label3D) visual.Layers|=Scenery;
        if(root.IsClass("Terrain3D"))
            foreach(var property in root.GetPropertyList())
                if(property["name"].AsString()=="render_layers")
                { root.Set("render_layers",root.Get("render_layers").AsUInt32()|Scenery); break; }
        foreach(var child in root.GetChildren()) IncludeScenery(child);
    }
}
