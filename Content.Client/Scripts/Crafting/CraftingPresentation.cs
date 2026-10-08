using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
namespace ProjectG.Crafting;
/// <summary>Buttons send intentions. Materials, stock, crafted items and operation IDs come from the server.</summary>
public partial class CraftingPresentation : Node3D
{
    private NetworkClient _network=null!;
    private PlayerController _player=null!;
    private PanelContainer _panel=null!;
    private VBoxContainer _content=null!;
    private Label _status=null!;
    private CraftState? _state;
    private CraftCommand? _pending;
    private double _retry;
    private readonly Dictionary<ushort,Node3D> _nodes=new();
    public void Initialize(NetworkClient network,PlayerController player)
    {
        _network=network; _player=player;
        _network.CraftStateReceived+=Apply; _network.CraftResultReceived+=Result;
        _network.ResourceNodeReceived+=Resource; _network.ResourceNodeLeft+=Remove;
        var canvas=new CanvasLayer { Layer=5 }; AddChild(canvas);
        _panel=new PanelContainer { AnchorLeft=0.5f,AnchorRight=0.5f,OffsetLeft=-260,OffsetRight=260,OffsetTop=70,Visible=false }; canvas.AddChild(_panel);
        var column=new VBoxContainer(); _panel.AddChild(column);
        column.AddChild(new Label { Text="Добыча и крафт [C]",HorizontalAlignment=HorizontalAlignment.Center });
        var scroll=new ScrollContainer { CustomMinimumSize=new(500,380) }; column.AddChild(scroll);
        _content=new VBoxContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; scroll.AddChild(_content);
        _status=new Label { Text="Ожидание сервера",AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new(500,45) }; column.AddChild(_status);
        var close=new Button { Text="Закрыть" }; close.Pressed+=()=>_panel.Hide(); column.AddChild(close);
        foreach(var resource in network.ResourceNodes.Values) Resource(resource);
        var station=network.CraftRecipes.Values.First();
        AddChild(new MeshInstance3D { Position=new(station.StationPosition.X,0.55f,station.StationPosition.Y),Mesh=new BoxMesh { Size=new(1.1f,0.2f,0.7f) },MaterialOverride=new StandardMaterial3D { AlbedoColor=new(0.55f,0.34f,0.16f) } });
        AddChild(new Label3D { Position=new(station.StationPosition.X,1.7f,station.StationPosition.Y),Text=station.StationName+" • C",Billboard=BaseMaterial3D.BillboardModeEnum.Enabled });
        Rebuild();
    }
    private void Apply(CraftState state)
    { if(state.OwnerId!=_player.EntityId) return; _state=state; Rebuild(); }
    private void Result(CraftResult result)
    {
        if(_pending is not { } command || result.Operation!=command.Operation) return;
        _pending=null;
        _status.Text=result.Outcome switch
        {
            CraftOutcome.Accepted=>"Сохранено. Материалы и предметы подтверждены сервером.",
            CraftOutcome.AlreadyProcessed=>"Эта операция уже сохранена. Ничего не выдано повторно.",
            CraftOutcome.TooFar=>"Подойди ближе к ресурсу или верстаку.",
            CraftOutcome.Busy=>"Остановись и закончи применение умения.",
            CraftOutcome.InvalidState=>"Персонаж не может действовать.",
            CraftOutcome.MissingMaterials=>"Недостаточно материалов.",
            CraftOutcome.InventoryFull=>"Освободи место в инвентаре.",
            CraftOutcome.MaterialFull=>"Запас материала заполнен.",
            CraftOutcome.Depleted=>"Общий запас ресурса исчерпан.",
            CraftOutcome.Cooldown=>"Действие ещё восстанавливается.",
            _=>"Действие отклонено: "+result.Outcome
        }; Rebuild();
    }
    private void Request(CraftAction action,ushort target)
    {
        if(_pending is not null || _state is not { } state || state.LastOperation>=long.MaxValue) return;
        _pending=new(state.LastOperation+1,action,target); _retry=0; _network.SendCraft(_pending.Value);
        _status.Text="Ожидание сохранения…"; Rebuild();
    }
    private void Rebuild()
    {
        if(_content is null) return;
        foreach(var child in _content.GetChildren()) { _content.RemoveChild(child); child.QueueFree(); }
        var names=new Dictionary<ushort,string>(); foreach(var recipe in _network.CraftRecipes.Values) foreach(var cost in recipe.Costs) names[cost.Id]=cost.Name;
        _content.AddChild(new Label { Text="Материалы" });
        if(_state is { } state && state.Materials.Count>0) foreach(var m in state.Materials) _content.AddChild(new Label { Text=$"{names.GetValueOrDefault(m.Id,"Материал")}: {m.Quantity}" });
        else _content.AddChild(new Label { Text="Пока пусто" });
        _content.AddChild(new Label { Text="Ближайшие ресурсы — общий запас" });
        foreach(var node in _network.ResourceNodes.Values.OrderBy(n=>n.Id))
        {
            var current=node;
            var button=new Button { Text=$"{node.Name}: {node.Remaining} — собрать",Disabled=_pending is not null || _state is null || node.Remaining==0 };
            button.Pressed+=()=>Request(CraftAction.Gather,current.Id); _content.AddChild(button);
        }
        _content.AddChild(new Label { Text="У верстака" });
        foreach(var recipe in _network.CraftRecipes.Values.OrderBy(r=>r.Id))
        {
            var current=recipe; _content.AddChild(new Label { Text=string.Join(", ",recipe.Costs.Select(c=>$"{c.Name} ×{c.Quantity}")) });
            var button=new Button { Text=recipe.Name+" → "+recipe.OutputName,Disabled=_pending is not null || _state is null };
            button.Pressed+=()=>Request(CraftAction.Make,current.Id); _content.AddChild(button);
        }
        _content.AddChild(new Label { Text="Подойди и остановись перед действием. Готовые вещи — в I." });
    }
    private void Resource(ResourceNodeState state)
    {
        if(!_nodes.TryGetValue(state.Id,out var node))
        {
            node=new Node3D { Position=new(state.Position.X,0,state.Position.Y) }; AddChild(node); _nodes.Add(state.Id,node);
            node.AddChild(new MeshInstance3D { Name="Shape",Position=new(0,0.35f,0),Mesh=new CylinderMesh { TopRadius=0.35f,BottomRadius=0.5f,Height=0.7f },MaterialOverride=new StandardMaterial3D { AlbedoColor=new(0.5f,0.4f,0.3f) } });
            node.AddChild(new Label3D { Name="Title",Position=new(0,1.5f,0),Billboard=BaseMaterial3D.BillboardModeEnum.Enabled });
        }
        node.GetNode<Label3D>("Title").Text=state.Name+(state.Remaining==0 ? " • истощено" : " • C");
        node.GetNode<MeshInstance3D>("Shape").Visible=state.Remaining>0; Rebuild();
    }
    private void Remove(ResourceNodeDespawn state) { if(_nodes.Remove(state.Id,out var node)) node.QueueFree(); Rebuild(); }
    public override void _Process(double delta)
    { if(_pending is { } command) { _retry+=delta; if(_retry>=2) { _retry=0; _network.SendCraft(command); } } }
    public override void _UnhandledInput(InputEvent input)
    { if(input is InputEventKey { Pressed:true,Echo:false,Keycode:Key.C }) { _panel.Visible=!_panel.Visible; GetViewport().SetInputAsHandled(); } }
    public override void _ExitTree()
    { if(_network is not null) { _network.CraftStateReceived-=Apply; _network.CraftResultReceived-=Result; _network.ResourceNodeReceived-=Resource; _network.ResourceNodeLeft-=Remove; } }
}
