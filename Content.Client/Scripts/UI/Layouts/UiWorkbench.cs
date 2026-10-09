using Godot;
namespace ProjectG.UI;

/// <summary>Development-only art/layout preview. Never connects or fabricates gameplay state.</summary>
public partial class UiWorkbench : Control
{
    private Control _host=null!;
    private UiScreenLayout? _layout;
    public override void _Ready()
    {
        Theme=GameUi.CreateTheme(); SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var backdrop=new ColorRect { Color=new("142737"),MouseFilter=MouseFilterEnum.Ignore }; AddChild(backdrop); backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin=new MarginContainer(); AddChild(margin); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach(var edge in new[]{"left","right","top","bottom"}) margin.AddThemeConstantOverride("margin_"+edge,16);
        var body=new VBoxContainer(); margin.AddChild(body);
        body.AddChild(GameUi.Text("UI WORKBENCH · подготовка исходников",22));
        body.AddChild(GameUi.Text("Слоты макетов и ресурсы темы. Игровые данные здесь не создаются.",14));
        var buttons=new HBoxContainer(); body.AddChild(buttons);
        string[] names=["HUD","Персонаж","Инвентарь","Карта","Поручения","Эхо","Гильдия","Диалог"];
        for(var i=0;i<names.Length;i++) { var kind=(UiScreenKind)i; buttons.AddChild(GameUi.Button(names[i],()=>Select(kind))); }
        buttons.AddChild(GameUi.Button("Компоненты",ShowComponents));
        _host=new Control { SizeFlagsVertical=SizeFlags.ExpandFill }; body.AddChild(_host);
        Select(UiScreenKind.Character);
    }
    private void ClearHost()
    {
        foreach(var child in _host.GetChildren()) { _host.RemoveChild(child); child.QueueFree(); }
        _layout=null;
    }
    public void ShowComponents()
    {
        ClearHost(); var scroll=new ScrollContainer(); _host.AddChild(scroll); scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var body=new VBoxContainer { Theme=GameUi.CreatePaperTheme(),SizeFlagsHorizontal=SizeFlags.ExpandFill }; scroll.AddChild(body);
        var paper=new PanelContainer(); body.AddChild(paper); var content=new VBoxContainer(); paper.AddChild(content);
        content.AddChild(GameUi.Text("Ресурсы Project G UI Source Kit",24));
        var states=new HBoxContainer(); content.AddChild(states);
        states.AddChild(GameUi.Button("Обычная кнопка",()=>{}));
        var selected=GameUi.Button("Выбрана",()=>{}); selected.ToggleMode=true; selected.ButtonPressed=true; states.AddChild(selected);
        var disabled=GameUi.Button("Недоступно",()=>{}); disabled.Disabled=true; states.AddChild(disabled);
        content.AddChild(new LineEdit { PlaceholderText="Поиск · проверка шрифта и фокуса" });
        var grid=new GridContainer { Columns=8 }; content.AddChild(grid);
        foreach(var key in UiAssets.Keys.Where(key=>key.StartsWith("icon.",StringComparison.Ordinal)).OrderBy(key=>key,StringComparer.Ordinal))
        {
            var cell=new VBoxContainer { CustomMinimumSize=new(104,94) }; grid.AddChild(cell);
            var icon=new UiIconSlot { SizeFlagsHorizontal=SizeFlags.ShrinkCenter }; cell.AddChild(icon); icon.Bind(key,key);
            var label=GameUi.Text(key[5..],12); label.HorizontalAlignment=HorizontalAlignment.Center; cell.AddChild(label);
        }
    }
    public void Select(UiScreenKind screen)
    {
        ClearHost();
        _layout=new UiScreenLayout { Screen=screen,ShowSlotGuides=true }; _host.AddChild(_layout);
        if(screen==UiScreenKind.Character)
        {
            var preview=new UiModelPreview { SizeFlagsVertical=SizeFlags.ExpandFill }; _layout.Slot("Model").AddChild(preview);
            preview.SetVisual(UiAssets.Model("preview.character"));
            _layout.Slot("Statistics").AddChild(GameUi.Text("Место для характеристик",16));
            var row=new HBoxContainer(); _layout.Slot("Statistics").AddChild(row);
            var icon=new UiIconSlot(); row.AddChild(icon); icon.Bind("equipment.weapon","Иконка категории оружия");
            var portrait=new UiPortrait(); row.AddChild(portrait); portrait.Bind("preview.portrait","?");
        }
    }
}
