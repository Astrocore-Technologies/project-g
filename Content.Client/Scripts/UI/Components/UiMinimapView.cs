using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.StarterZone;
namespace ProjectG.UI;

/// <summary>Small shared-world terrain capture plus owner exploration mask. No world state is created here.</summary>
public partial class UiMinimapView : Control
{
    private SubViewport _terrain=null!;
    private Camera3D _camera=null!;
    private StarterMap _map=null!;
    private Label _location=null!;
    public StarterMap Map => _map;
    public override void _Ready()
    {
        _terrain=GetNode<SubViewport>("%Terrain"); _terrain.World3D=GetViewport().World3D;
        _camera=GetNode<Camera3D>("%Camera"); _map=GetNode<StarterMap>("%Map");
        GetNode<TextureRect>("%Ground").Texture=_terrain.GetTexture(); _map.HasTerrainRender=true;
        _location=GetNode<Label>("%Location");
    }
    public void Update(NetworkClient network,StarterZoneState zone,ExplorationState? exploration,System.Numerics.Vector2 position,float height,bool visible)
    {
        Visible=visible;
        if(!visible) { _terrain.RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled; return; }
        _map.Update(network,zone,exploration,position);
        _camera.Position=new(position.X,height+120,position.Y); _camera.Size=_map.RadiusMeters*2;
        _camera.CullMask=network.Navigation?.Surface is not null?UiMapLayers.Scenery:1;
        _terrain.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
        _location.Text=zone.RegionName;
        TooltipText=exploration is null?"Загрузка карты…":"Карта · M";
    }
}
