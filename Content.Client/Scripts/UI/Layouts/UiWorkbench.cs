using Godot;
namespace ProjectG.UI;

/// <summary>Offline inspector of the exact production scenes. No network, secrets or gameplay simulation.</summary>
public partial class UiWorkbench : Control
{
    public static readonly string[] Screens=["CharacterView","InventoryView","WorldMapView","JournalView","EchoView","GuildView","DialogueView","ProfessionView","CraftingView","EconomyView","TradeView","SocialView","PvpView","KeeperView","CombatJournalView","DeathView","GuideView","EmoteView","Settings","PauseMenuView","PlayerContextView","PlayerHudView"];
    private OptionButton _picker=null!;
    private Control? _host;
    public Control? CurrentView {get;private set;}
    public override void _Ready()
    {
        _picker=GetNode<OptionButton>("%ScreenPicker");foreach(var screen in Screens)_picker.AddItem(screen);
        _picker.ItemSelected+=Choose;ShowScene("CharacterView");
    }
    private void Choose(long index)=>ShowScene(Screens[index]);
    public void ShowScene(string screen)
    {
        if(!Screens.Contains(screen))throw new ArgumentOutOfRangeException(nameof(screen));
        if(_host is not null){RemoveChild(_host);_host.QueueFree();}
        var view=GD.Load<PackedScene>("res://UI/Screens/"+screen+".tscn").Instantiate<Control>();CurrentView=view;
        if(view is ClientSettingsPanel settings)settings.SettingsPath="res://../.artifacts/ui-final-2026-10-18/workbench-settings.cfg";
        if(screen is "PlayerHudView" or "PlayerContextView")
        {_host=view;AddChild(view);view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);}
        else
        {
            var shell=new UiWindow();_host=shell;AddChild(shell);shell.Build(screen,new(1180,656));shell.Body.AddChild(view);
            shell.Show();shell.CloseRequested+=shell.Hide;
        }
        MoveChild(GetNode("Toolbar"),GetChildCount()-1);_picker.Select(Array.IndexOf(Screens,screen));
    }
    public void Select(UiScreenKind screen)=>ShowScene(screen switch
    {UiScreenKind.Hud=>"PlayerHudView",UiScreenKind.Character=>"CharacterView",UiScreenKind.Inventory=>"InventoryView",UiScreenKind.WorldMap=>"WorldMapView",UiScreenKind.Journal=>"JournalView",UiScreenKind.Echo=>"EchoView",UiScreenKind.Guild=>"GuildView",_=>"DialogueView"});
    public void ShowComponents()=>ShowScene("CraftingView");
    public override void _ExitTree(){if(_picker is not null)_picker.ItemSelected-=Choose;}
}
