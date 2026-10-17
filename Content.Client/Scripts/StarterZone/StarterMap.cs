using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
namespace ProjectG.StarterZone;

public partial class StarterMap : Control
{
    [Export] public bool FollowPlayer { get; set; }
    [Export(PropertyHint.Range,"12,120,1")] public float RadiusMeters { get; set; }=40;
    public bool HasTerrainRender { get; set; }
    public Texture2D? TerrainTexture { get; set; }
    public bool TerrainMatchesView { get; set; }
    public event Action? ViewChanged;
    public override void _Notification(int what)
    {
        if(what!=NotificationResized) return;
        var old=Math.Min(_previousSize.X,_previousSize.Y);
        if(old>0) _pan*=Math.Min(Size.X,Size.Y)/old;
        _previousSize=Size; QueueRedraw(); ViewChanged?.Invoke();
    }
    public Rect2 WorldView
    {
        get
        {
            if(_network?.Navigation is not {} grid) return default;
            return new(new Vector2(grid.Origin.X,grid.Origin.Y)-Offset/CellScale*grid.CellSize,Size/CellScale*grid.CellSize);
        }
    }
    public Vector2 ProjectPosition(System.Numerics.Vector2 position) => Point(position);
    private NetworkClient? _network;
    private StarterZoneState _zone;
    private ExplorationState? _state;
    private System.Numerics.Vector2 _position;
    private float _zoom=1;
    private Vector2 _pan;
    private Vector2 _previousSize;
    public bool Interactive { get; set; }
    public bool ShowPlaces { get; set; }=true;
    public System.Numerics.Vector2? Pin { get; set; }
    public event Action<System.Numerics.Vector2>? PinChanged;
    public void Update(NetworkClient network,StarterZoneState zone,ExplorationState? state,System.Numerics.Vector2 position)
    { _network=network; _zone=zone; _state=state; _position=position; QueueRedraw(); }
    private float CellScale => _network?.Navigation is {} g ? FollowPlayer ? Math.Min(Size.X,Size.Y)*g.CellSize/(2*Math.Max(1,RadiusMeters)) : Mathf.Min(Size.X/g.Width,Size.Y/g.Height)*_zoom:1;
    private Vector2 Offset => _network?.Navigation is {} g ? FollowPlayer ? Size/2-new Vector2((_position.X-g.Origin.X)/g.CellSize,(_position.Y-g.Origin.Y)/g.CellSize)*CellScale : (Size-new Vector2(g.Width,g.Height )*CellScale)*.5f+_pan:Vector2.Zero;
    private Vector2 Point(System.Numerics.Vector2 p) => _network?.Navigation is {} g ? Offset+new Vector2((p.X-g.Origin.X)/g.CellSize,(p.Y-g.Origin.Y)/g.CellSize )*CellScale:Vector2.Zero;
    private bool Explored(System.Numerics.Vector2 p)
    { if(_network?.Navigation is not {} g || _state is not {} s) return false; var c=g.Cell(p); return c>=0 && c<g.CellCount && c/8<s.Cells.Length && (s.Cells[c/8]&(1<<(c%8)))!=0; }
    public void ZoomBy(float value) { _zoom=Mathf.Clamp(_zoom*value,1,32); if(_zoom==1) _pan=Vector2.Zero; QueueRedraw(); ViewChanged?.Invoke(); }
    public void ResetView() { _zoom=1; _pan=Vector2.Zero; QueueRedraw(); ViewChanged?.Invoke(); }
    public void FocusAt(System.Numerics.Vector2 p)
    { if(_network?.Navigation is {} grid) _zoom=Mathf.Clamp(Math.Max(grid.Width,grid.Height)*grid.CellSize/160f,1,32); _pan=Vector2.Zero; _pan=Size/2-Point(p); QueueRedraw(); ViewChanged?.Invoke(); }
    public override void _GuiInput(InputEvent ev)
    {
        if(!Interactive || _network?.Navigation is not {} grid) return;
        if(ev is InputEventMouseMotion motion && motion.ButtonMask.HasFlag(MouseButtonMask.Right)) { _pan+=motion.Relative; _pan=_pan.Clamp(-Size*_zoom,Size*_zoom); QueueRedraw(); ViewChanged?.Invoke(); AcceptEvent(); }
        if(ev is not InputEventMouseButton { Pressed:true } click) return;
        if(click.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) { ZoomBy(click.ButtonIndex==MouseButton.WheelUp?1.2f:1/1.2f); AcceptEvent(); }
        if(click.ButtonIndex!=MouseButton.Left) return;
        var c=(click.Position-Offset)/CellScale;
        var p=new System.Numerics.Vector2(grid.Origin.X+c.X*grid.CellSize,grid.Origin.Y+c.Y*grid.CellSize);
        if(Explored(p)) { Pin=p; PinChanged?.Invoke(p); QueueRedraw(); } AcceptEvent();
    }
    public override void _Draw()
    {
        if(!HasTerrainRender || _network?.Navigation is null || _state is null) DrawRect(new(Vector2.Zero,Size),new Color("10282f"));
        if(_network?.Navigation is not {} grid || _state is not {} state) return;
        var scale=CellScale; var offset=Offset;
        if(TerrainTexture is not null) DrawTextureRect(TerrainTexture,TerrainMatchesView?new(Vector2.Zero,Size):new(offset,new Vector2(grid.Width,grid.Height)*scale),false);
        var minX=Math.Max(0,(int)Math.Floor(-offset.X/scale)); var maxX=Math.Min(grid.Width-1,(int)Math.Ceiling((Size.X-offset.X)/scale));
        var minZ=Math.Max(0,(int)Math.Floor(-offset.Y/scale)); var maxZ=Math.Min(grid.Height-1,(int)Math.Ceiling((Size.Y-offset.Y)/scale));
        if(HasTerrainRender)
        {
            var a=offset.Clamp(Vector2.Zero,Size); var b=(offset+new Vector2(grid.Width,grid.Height)*scale).Clamp(Vector2.Zero,Size);
            var fog=new Color("10282f");
            DrawRect(new(Vector2.Zero,new Vector2(Size.X,a.Y)),fog); DrawRect(new(new Vector2(0,b.Y),new Vector2(Size.X,Size.Y-b.Y)),fog);
            DrawRect(new(new Vector2(0,a.Y),new Vector2(a.X,Math.Max(0,b.Y-a.Y))),fog); DrawRect(new(new Vector2(b.X,a.Y),new Vector2(Size.X-b.X,Math.Max(0,b.Y-a.Y))),fog);
        }
        for(var z=minZ;z<=maxZ;z++) for(var x=minX;x<=maxX;x++)
        {
            var cell=z*grid.Width+x; var known=cell/8<state.Cells.Length && (state.Cells[cell/8]&(1<<(cell%8)))!=0;
            if(HasTerrainRender && known)
            {
                var rect=new Rect2(offset+new Vector2(x,z)*scale,new(scale+.35f,scale+.35f));
                if(!KnownCell(grid,state,x-1,z)) FadeFog(rect,Vector2.Right);
                if(!KnownCell(grid,state,x+1,z)) FadeFog(rect,Vector2.Left);
                if(!KnownCell(grid,state,x,z-1)) FadeFog(rect,Vector2.Down);
                if(!KnownCell(grid,state,x,z+1)) FadeFog(rect,Vector2.Up);
                continue;
            }
            if(!HasTerrainRender && !known) continue;
            DrawRect(new(offset+new Vector2(x,z)*scale,new(scale+.35f,scale+.35f)),!known?new Color("10282f"):grid.IsBlocked(x,z)?new Color("3d6257"):new Color("698b63"));
        }
        if(ShowPlaces)
        {
            if(Explored(_zone.TownPosition)) Marker(_zone.TownPosition,_zone.TownName,new("b68a43"));
            foreach(var place in state.Places) Marker(place.Position,place.Name,new("387957"));
            if(_network.CurrentRegion is {} region && Explored(region.Exit)) Marker(region.Exit,"Переход",new("5095af"));
            if(_network.LatestWorldNode is {} node && Explored(node.Position)) Marker(node.Position,"Хранитель",new("806994"));
        }
        if(Pin is {} pin && Explored(pin)) { var q=Point(pin); DrawCircle(q,8,new Color("a1443d"),false,2); DrawLine(q-Vector2.Up*12,q+Vector2.Up*12,new Color("a1443d"),2); }
        var pos=Point(_position);
        DrawColoredPolygon(new[]{pos+new Vector2(0,-10),pos+new Vector2(7,8),pos+new Vector2(0,4),pos+new Vector2(-7,8)},new Color("fff5d9"));
        if(!FollowPlayer) DrawString(ThemeDB.FallbackFont,new(10,22),"С",HorizontalAlignment.Left,-1,16,new Color("eee2c6"));
    }
    private void Marker(System.Numerics.Vector2 p,string text,Color color)
    {
        var point=Point(p); DrawCircle(point,5,color); DrawCircle(point,6,new Color("fff8e8"),false,1.5f);
        if(Interactive)
        {
            DrawString(ThemeDB.FallbackFont,point+new Vector2(10,6),text,HorizontalAlignment.Left,-1,14,new Color("10282f"));
            DrawString(ThemeDB.FallbackFont,point+new Vector2(9,5),text,HorizontalAlignment.Left,-1,14,new Color("f1e9d6"));
        }
    }
    private static bool KnownCell(Content.Shared.Navigation.NavigationGrid grid,ExplorationState state,int x,int z)
    { var i=z*grid.Width+x; return x>=0 && z>=0 && x<grid.Width && z<grid.Height && i/8<state.Cells.Length && (state.Cells[i/8]&(1<<(i%8)))!=0; }
    private void FadeFog(Rect2 cell,Vector2 inward)
    {
        var depth=Math.Min(8,Math.Min(cell.Size.X,cell.Size.Y)*.4f);
        for(var layer=0;layer<4;layer++)
        {
            var width=depth*(1-layer*.25f); var color=new Color("10282f"); color.A=.2f+layer*.12f;
            var p=cell.Position; var size=cell.Size;
            if(inward.X!=0) { size.X=width; if(inward.X<0) p.X=cell.End.X-width; }
            else { size.Y=width; if(inward.Y<0) p.Y=cell.End.Y-width; }
            DrawRect(new(p,size),color);
        }
    }
}
