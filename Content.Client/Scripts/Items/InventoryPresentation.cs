using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Items;

/// <summary>Owner-only equipment grid. Selected item and artwork are presentation; operations remain authoritative.</summary>
public partial class InventoryPresentation : CanvasLayer
{
    private NetworkClient _network=null!;
    private UiWindow _window=null!;
    private GridContainer _grid=null!;
    private Label _status=null!, _name=null!, _stats=null!, _condition=null!, _count=null!;
    private Button _equip=null!, _revive=null!;
    private UiIconSlot _selectedIcon=null!;
    private InventoryState _state;
    private ItemConditionState? _latestCondition;
    private NetworkEntityId _owner;
    private ulong _selected;
    private uint _sequence,_reviveSequence;
    private bool _waiting,_awaitingRevive,_alive=true;
    private string _search=""; private int _category;
    private CraftState _materials; private ushort _materialSelected;
    private Label _coins=null!, _comparison=null!;
    private double _sentAt;

    public void Initialize(NetworkClient network,NetworkEntityId owner)
    {
        _network=network; _owner=owner; Layer=30; network.ItemConditionReceived+=Condition; network.EconomyStateReceived+=Economy; network.CraftStateReceived+=Materials;
        _window=new UiWindow { ToggleKey=Key.I }; AddChild(_window); _window.Build("Инвентарь",new(1180,656)); _window.CloseRequested+=_window.Close;
        var page=UiComposition.Page(_window,"inventory");
        var search=new LineEdit { PlaceholderText="Поиск по названию…",ClearButtonEnabled=true }; page.AddChild(search);
        search.TextChanged+=value=> { _search=value; Rebuild(); };
        var categories=new HBoxContainer(); page.AddChild(categories);
        string[] names=["Все","Оружие","Броня","Материалы"];
        var categoryGroup=new ButtonGroup();
        for(var i=0;i<names.Length;i++) { var index=i; var button=GameUi.Button(names[i],()=> { _category=index; Rebuild(); }); button.ToggleMode=true; button.ButtonGroup=categoryGroup; button.ButtonPressed=i==0; if(UiAssets.Skin.TabSelected is {} selected) button.AddThemeStyleboxOverride("pressed",selected); button.AddThemeColorOverride("font_pressed_color",UiAssets.Skin.Ink); categories.AddChild(button); }
        _count=GameUi.Text("Экипировка",14); page.AddChild(_count);
        var columns=new HBoxContainer { SizeFlagsVertical=Control.SizeFlags.ExpandFill }; page.AddChild(columns);
        var scroll=new ScrollContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled }; columns.AddChild(scroll);
        _grid=new GridContainer { Columns=4,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; scroll.AddChild(_grid);
        var detail=new PanelContainer { CustomMinimumSize=new(300,0),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; columns.AddChild(detail); var body=new VBoxContainer(); detail.AddChild(body);
        _selectedIcon=new UiIconSlot { Disabled=true,CustomMinimumSize=new(72,72),SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin }; body.AddChild(_selectedIcon);
        _name=GameUi.Text("Выберите предмет",20); _name.AutowrapMode=TextServer.AutowrapMode.WordSmart; body.AddChild(_name);
        _stats=GameUi.Text(""); body.AddChild(_stats); _condition=GameUi.Text("",14); _condition.AutowrapMode=TextServer.AutowrapMode.WordSmart; body.AddChild(_condition);
        _comparison=UiComposition.Paragraph(body,"",14);
        var space=new Control { SizeFlagsVertical=Control.SizeFlags.ExpandFill }; body.AddChild(space);
        _equip=GameUi.Button("Надеть",Request); _equip.Disabled=true; body.AddChild(_equip);
        _coins=GameUi.Text("Монеты: —",14); page.AddChild(_coins);
        _status=GameUi.Text("Ожидание сервера…",14); _status.AutowrapMode=TextServer.AutowrapMode.WordSmart; page.AddChild(_status);
        _revive=GameUi.Button("Оживить (Development)",RequestRevive); _revive.Visible=network.CanDevelopmentRevive; _revive.Disabled=true; page.AddChild(_revive);
    }
    public void Apply(InventoryState state)
    {
        if(state.EntityId!=_owner) return; _state=state; Rebuild();
    }
    private void Rebuild()
    {
        var state=_state; if(state.Items is null) return;
        foreach(var child in _grid.GetChildren()) { _grid.RemoveChild(child); child.QueueFree(); }
        _count.Text=$"Снаряжение: {state.Items.Count} / {NetworkConstants.MaxInventoryItems} · Материалы хранятся отдельно";
        if(!state.Items.Any(item=>item.Handle==_selected)) _selected=state.Items.Count>0?state.Items[0].Handle:0;
        var visible=state.Items.Where(item=>item.Name.Contains(_search,StringComparison.OrdinalIgnoreCase) && (_category==0 || _category<3 && (int)item.Slot==_category)).ToArray();
        foreach(var item in visible)
        {
            var cell=new VBoxContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(68,104) }; _grid.AddChild(cell);
            var icon=new UiIconSlot { CustomMinimumSize=new(84,84),ToggleMode=true,ButtonPressed=item.Handle==_selected,SizeFlagsHorizontal=Control.SizeFlags.ShrinkCenter }; cell.AddChild(icon);
            icon.Bind(UiAssets.EquipmentKey(item.Slot),item.Name,item.Equipped?"✓":"");
            icon.Pressed+=()=> { _materialSelected=0; _selected=item.Handle; Apply(_state); };
            var label=GameUi.Text(item.Name,12); label.CustomMinimumSize=new(64,0); label.AutowrapMode=TextServer.AutowrapMode.WordSmart; cell.AddChild(label);
        }
        var materialCount=0;
        if(_category is 0 or 3) foreach(var material in _materials.Materials??Array.Empty<MaterialAmount>())
        {
            var name=MaterialName(material.Id); if(!name.Contains(_search,StringComparison.OrdinalIgnoreCase)) continue; materialCount++;
            var cell=new VBoxContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(68,104) }; _grid.AddChild(cell);
            var icon=new UiIconSlot { CustomMinimumSize=new(84,84),ToggleMode=true,ButtonPressed=_materialSelected==material.Id,SizeFlagsHorizontal=Control.SizeFlags.ShrinkCenter }; cell.AddChild(icon);
            icon.Bind("icon.leaf",name,material.Quantity.ToString()); var id=material.Id; icon.Pressed+=()=> { _materialSelected=id; Rebuild(); };
            var label=GameUi.Text(name,12); label.CustomMinimumSize=new(64,0); label.AutowrapMode=TextServer.AutowrapMode.WordSmart; cell.AddChild(label);
        }
        if(visible.Length+materialCount==0) _grid.AddChild(GameUi.Text(state.Items.Count==0?"Здесь пока пусто":"Ничего не найдено",14));
        if(_search.Length==0 && _category==0) for(var i=state.Items.Count;i<NetworkConstants.MaxInventoryItems;i++)
        {
            var cell=new VBoxContainer { CustomMinimumSize=new(68,104),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; _grid.AddChild(cell);
            var empty=new PanelContainer { CustomMinimumSize=new(84,84),SizeFlagsHorizontal=Control.SizeFlags.ShrinkCenter,TooltipText="Свободное место для снаряжения" };
            if(UiAssets.Skin.SlotNormal is {} style) empty.AddThemeStyleboxOverride("panel",style); cell.AddChild(empty);
        }
        RefreshDetail(); _status.Text=_waiting?"Ожидание подтверждения…":"";
    }
    private void RefreshDetail()
    {
        if(_materialSelected!=0 && _materials.Materials?.FirstOrDefault(m=>m.Id==_materialSelected) is {} material && material.Quantity>0)
        {
            _name.Text=MaterialName(material.Id); _stats.Text=$"Материал\nВ наличии: {material.Quantity}"; _condition.Text="Материалы используются в ремесле.\nРецепты и ремонт — C."; _comparison.Text=""; _equip.Disabled=true; _equip.Text="Материал"; _selectedIcon.Bind("icon.leaf",_name.Text); return;
        }
        _materialSelected=0;
        var item=Selected(); _equip.Disabled=_waiting || !_alive || item is null;
        if(item is not {} selected) { _name.Text="Выберите предмет"; _stats.Text=""; _condition.Text=""; _comparison.Text=""; _selectedIcon.Bind("equipment.unknown","Нет предмета"); return; }
        _name.Text=selected.Name; _selectedIcon.Bind(UiAssets.EquipmentKey(selected.Slot),selected.Name);
        _stats.Text=$"Атака {selected.AttackBonus:+0.##;-0.##;0}\nЗащита {selected.DefenseBonus:+0.##;-0.##;0}\nЗдоровье {selected.HealthBonus:+0.##;-0.##;0}";
        _equip.Text=selected.Equipped?"Снять":"Надеть"; _condition.Text="";
        var equipped=_state.Items.FirstOrDefault(other=>other.Equipped && other.Slot==selected.Slot);
        _comparison.Text=selected.Equipped?"Сейчас экипирован":equipped.Handle==0?"В этом слоте ничего не надето":$"По сравнению с: {equipped.Name}\nАтака {selected.AttackBonus-equipped.AttackBonus:+0.##;-0.##;0}  ·  Защита {selected.DefenseBonus-equipped.DefenseBonus:+0.##;-0.##;0}\nЗдоровье {selected.HealthBonus-equipped.HealthBonus:+0.##;-0.##;0}";
        if(_latestCondition is {} state) foreach(var condition in state.Items) if(condition.Handle==selected.Handle)
            _condition.Text=$"Прочность {condition.Current}/{condition.Maximum}"+(condition.Current==0?"\nТребуется ремонт · C":"\nРемонт · C");
    }
    private InventoryEntry? Selected()
    { if(_state.Items is not null) foreach(var item in _state.Items) if(item.Handle==_selected) return item; return null; }
    private void Condition(ItemConditionState state) { if(state.OwnerId!=_owner) return; _latestCondition=state; RefreshDetail(); }
    private void Request()
    {
        if(!_alive || _waiting || Selected() is not {} item) return;
        if(++_sequence==0) _sequence++; _waiting=true; _sentAt=Time.GetTicksMsec()/1000d; _status.Text="Ожидание подтверждения…"; RefreshDetail();
        _network.SendInventory(new(_sequence,item.Equipped?InventoryAction.Unequip:InventoryAction.Equip,item.Handle));
    }
    public void Result(InventoryResult result)
    { if(result.Sequence!=_sequence) return; _waiting=false; RefreshDetail(); _status.Text=result.Outcome==InventoryOutcome.Accepted?"Изменения сохранены":$"Действие отклонено: {result.Outcome}"; }
    public void PickupResult(PickupResult result) => _status.Text=result.Outcome switch
    {
        PickupOutcome.Accepted=>"Предмет подобран", PickupOutcome.Channeling=>"Подбор: стойте 5 секунд с активным PvP-тегом",
        PickupOutcome.Interrupted=>"Подбор прерван действием или уроном", PickupOutcome.InvalidState=>"Для PvP-вещи нужен активный тег; остановитесь и завершите другие действия",
        _=>$"Подбор отклонён: {result.Outcome}"
    };
    public void ApplyAlive(bool alive) { _alive=alive; RefreshDetail(); _revive.Disabled=alive || !_network.CanDevelopmentRevive; if(alive && _awaitingRevive) { _awaitingRevive=false; _status.Text="Персонаж оживлён"; } }
    private void RequestRevive()
    { if(_revive.Disabled) return; if(++_reviveSequence==0) _reviveSequence++; _revive.Disabled=true; _awaitingRevive=true; _status.Text="Ожидание оживления…"; _network.SendDevelopmentRevive(new(_reviveSequence)); }
    public void Toggle() { if(_window.Visible) _window.Close(); else _window.Open(); }
    public override void _UnhandledInput(InputEvent ev)
    { if(ev is InputEventKey { Pressed:true,Echo:false,PhysicalKeycode:Key.I }) { Toggle(); GetViewport().SetInputAsHandled(); } }
    private string MaterialName(ushort id) => _network.CraftRecipes.Values.SelectMany(r=>r.Costs).FirstOrDefault(m=>m.Id==id).Name??$"Материал {id}";
    private void Materials(CraftState state) { if(state.OwnerId!=_owner) return; _materials=state; Rebuild(); }
    private void Economy(EconomyState state) { if(state.OwnerId==_owner) _coins.Text=$"Монеты: {state.Coins:N0}"; }
    public override void _Process(double delta) { if(_waiting && Time.GetTicksMsec()/1000d-_sentAt>10) { _waiting=false; RefreshDetail(); _status.Text="Нет ответа сервера. Попробуйте снова."; } }
    public override void _ExitTree() { if(_network is not null) { _network.ItemConditionReceived-=Condition; _network.EconomyStateReceived-=Economy; _network.CraftStateReceived-=Materials; } }
}
