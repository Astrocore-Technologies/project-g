using Godot;
namespace ProjectG.UI;

/// <summary>Public presentation key, independent of a localized name or runtime entity handle.</summary>
[GlobalClass]
public partial class UiArtEntry : Resource
{
    [Export] public string Key { get; set; } = "";
    [Export] public Texture2D? Texture { get; set; }
    [Export] public PackedScene? Model { get; set; }
    [Export] public string Attribution { get; set; } = "";
}
