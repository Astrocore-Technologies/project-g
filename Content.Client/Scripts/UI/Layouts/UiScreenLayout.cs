using Godot;
namespace ProjectG.UI;

public enum UiScreenKind { Hud,Character,Inventory,WorldMap,Journal,Echo,Guild,Dialogue }

/// <summary>Named, reusable content slots. Contains no sample quests, names, stats, rewards or network commands.</summary>
public partial class UiScreenLayout : Control
{
    [Export] public UiScreenKind Screen { get; set; }
    [Export] public bool ShowSlotGuides { get; set; }
    [Export] public float SidebarWidth { get; set; }=170;
    [Export] public float DetailsWidth { get; set; }=300;
    private readonly Dictionary<string,Control> _slots=new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string,Control> Slots=>_slots;
    public Control Slot(string name)=>_slots.TryGetValue(name,out var slot)?slot:throw new ArgumentException($"Unknown {Screen} slot: {name}");
    public override void _Ready()
    {
        Theme=GameUi.CreatePaperTheme(); MouseFilter=MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if(Screen==UiScreenKind.Hud) { Hud(); return; }
        var panel=new PanelContainer(); AddChild(panel); panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var body=new VBoxContainer(); panel.AddChild(body); AddSlot(body,"Header",new(0,40));
        var row=new HBoxContainer { SizeFlagsVertical=SizeFlags.ExpandFill }; body.AddChild(row);
        switch(Screen)
        {
            case UiScreenKind.Character:
                AddSlot(row,"Navigation",new(SidebarWidth,0)); AddSlot(row,"Equipment",new(68,0)); AddSlot(row,"Model",Vector2.Zero,true); AddSlot(row,"Statistics",new(DetailsWidth,0)); break;
            case UiScreenKind.Inventory:
                var inventory=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill }; row.AddChild(inventory);
                AddSlot(inventory,"Categories",new(0,42)); AddSlot(inventory,"Items",Vector2.Zero,true); AddSlot(inventory,"Currency",new(0,36)); AddSlot(row,"ItemDetails",new(DetailsWidth,0)); break;
            case UiScreenKind.WorldMap:
                AddSlot(row,"Legend",new(SidebarWidth,0)); AddSlot(row,"Map",Vector2.Zero,true); AddSlot(row,"LocationDetails",new(DetailsWidth,0)); break;
            case UiScreenKind.Journal: case UiScreenKind.Guild:
                AddSlot(row,"Navigation",new(SidebarWidth,0)); AddSlot(row,"Entries",new(240,0)); AddSlot(row,"EntryDetails",Vector2.Zero,true); break;
            case UiScreenKind.Echo:
                AddSlot(row,"Portraits",new(72,0)); AddSlot(row,"Navigation",new(SidebarWidth,0)); AddSlot(row,"Model",Vector2.Zero,true); AddSlot(row,"EchoDetails",new(DetailsWidth,0)); break;
            case UiScreenKind.Dialogue:
                AddSlot(row,"SpeakerVisual",Vector2.Zero,true); var conversation=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill }; row.AddChild(conversation);
                AddSlot(conversation,"SpeakerName",new(0,50)); AddSlot(conversation,"Speech",Vector2.Zero,true); AddSlot(conversation,"Choices",new(0,170)); break;
        }
        AddSlot(body,"Footer",new(0,38));
    }
    private void AddSlot(Node parent,string key,Vector2 minimum,bool expand=false)
    {
        var panel=new PanelContainer { Name=key,CustomMinimumSize=minimum,MouseFilter=MouseFilterEnum.Ignore,SizeFlagsHorizontal=expand?SizeFlags.ExpandFill:SizeFlags.Fill,SizeFlagsVertical=expand?SizeFlags.ExpandFill:SizeFlags.Fill };
        panel.AddThemeStyleboxOverride("panel",ShowSlotGuides?GameUi.Box(new Color(.1f,.2f,.3f,.07f),8):new StyleBoxEmpty()); parent.AddChild(panel);
        var content=new VBoxContainer { Name="Content",MouseFilter=MouseFilterEnum.Ignore }; panel.AddChild(content); _slots.Add(key,content);
        if(ShowSlotGuides) { var caption=GameUi.Text(key,14); caption.Modulate=new Color("827463"); content.AddChild(caption); }
    }
    private void Hud()
    {
        AddHudSlot("Minimap",new(20,20,180,180)); AddHudSlot("Location",new(210,40,200,90));
        AddHudSlot("QuestTracker",new(20,220,300,130));
        AddHudSlot("Navigation",new(-470,20,450,64),1,0);
        AddHudSlot("EchoPortraits",new(-100,170,80,260),1,0);
        AddHudSlot("Messages",new(20,-200,290,180),0,1);
        AddHudSlot("Resources",new(-180,-195,360,72),.5f,1);
        AddHudSlot("SkillBar",new(-320,-110,640,90),.5f,1);
        AddHudSlot("Interaction",new(-260,-90,240,70),1,1);
    }
    private void AddHudSlot(string key,Rect2 rect,float x=0,float y=0)
    {
        AddSlot(this,key,Vector2.Zero); var panel=(Control)_slots[key].GetParent();
        panel.AnchorLeft=panel.AnchorRight=x; panel.AnchorTop=panel.AnchorBottom=y;
        panel.OffsetLeft=rect.Position.X;panel.OffsetTop=rect.Position.Y;panel.OffsetRight=rect.End.X;panel.OffsetBottom=rect.End.Y;
    }
}
