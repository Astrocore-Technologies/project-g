using Godot;
using ProjectG.UI;
namespace ProjectG.Tests.UI;

public partial class UiFoundationSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode=Window.ModeEnum.Windowed; GetWindow().Size=new(1280,720); await Frames(3);
            var catalog=GD.Load<UiArtCatalog>(UiAssets.CatalogPath);
            Check(catalog is not null && catalog.Validate().Length==0,"Bundled art catalog is invalid.");
            Check(catalog!.Entries.Count>0,"Supplied source kit catalog is empty.");
            foreach(var entry in catalog.Entries) Check(entry.Texture is not null && entry.Texture.GetWidth()>0,"Failed to import "+entry.Key);
            Check(UiAssets.Skin.PaperPanel is StyleBoxTexture && UiAssets.Skin.ButtonNormal is StyleBoxTexture,"Source kit component textures missing.");
            var bad=new UiArtCatalog(); bad.Entries.Add(new UiArtEntry { Key="same" }); bad.Entries.Add(new UiArtEntry { Key="same" });
            Check(bad.Validate().Any(x=>x.Contains("Duplicate")) && bad.Validate().Any(x=>x.Contains("No resource")),"Missing/duplicate resources were not diagnosed.");
            Check(UiAssets.Texture("not-supplied") is null && UiAssets.Model("not-supplied") is null,"Missing art invented a resource.");
            Check(ReferenceEquals(GameUi.CreateTheme(),GameUi.CreateTheme()),"Theme rebuilt instead of cached.");
            var icons=new HBoxContainer(); AddChild(icons);
            var regular=new UiIconSlot { SizeFlagsVertical=Control.SizeFlags.ShrinkCenter }; regular.Bind("nav.character","Regular icon"); icons.AddChild(regular);
            var compact=new UiIconSlot { CustomMinimumSize=new(32,32),SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,FocusMode=Control.FocusModeEnum.None }; compact.Bind("nav.character","Compact icon"); icons.AddChild(compact);
            await Frames(3);
            Check(regular.Size.IsEqualApprox(new(64,64)) && compact.Size.IsEqualApprox(regular.Size/2),"Compact icon is not half the default slot size.");
            Check(compact.FocusMode==Control.FocusModeEnum.None,"Binding reset the caller's focus mode.");
            var regularArt=regular.GetChildren().OfType<TextureRect>().Single(); var compactArt=compact.GetChildren().OfType<TextureRect>().Single();
            Check(compactArt.Size.IsEqualApprox(regularArt.Size/2),"Compact artwork did not preserve half-size proportions.");
            icons.QueueFree(); await Frames(2);
            var workbench=GD.Load<PackedScene>("res://UI/Workbench.tscn").Instantiate<UiWorkbench>(); AddChild(workbench); await Frames(4);
            foreach(var screen in Enum.GetValues<UiScreenKind>())
            {
                workbench.Select(screen); await Frames(4);
                var layout=workbench.FindChildren("*","",true,false).OfType<UiScreenLayout>().Single();
                Check(layout.Slots.Count>=5,$"Missing slots in {screen}");
                foreach(var slot in layout.Slots.Values) Check(slot.Size.X>0 && slot.Size.Y>0,$"Collapsed {screen}/{slot.Name}");
                await Capture("layout-"+screen);
            }
            workbench.ShowComponents(); await Frames(4); await Capture("source-kit-components");
            workbench.Select(UiScreenKind.Character); await Frames(3);
            var preview=workbench.FindChildren("*","",true,false).OfType<UiModelPreview>().Single();
            preview.SetMesh(new CapsuleMesh { Radius=.3f,Height=1.6f },new StandardMaterial3D { AlbedoColor=new("6f8494") }); await Frames(3);
            var viewport=preview.GetChildren().OfType<SubViewport>().Single(); Check(viewport.OwnWorld3D,"Preview reused gameplay world.");
            await Capture("workbench-character");
            GetWindow().Size=new(1920,1080); await Frames(4); await Capture("workbench-1920");
            var root=new Control(); AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var original=GameUi.Button("Original focus",()=>{}); root.AddChild(original); original.GrabFocus(); await Frames(1);
            var modal=new UiWindow(); root.AddChild(modal); modal.Build("Modal smoke",new(640,400)); modal.CloseRequested+=modal.Close;
            var second=new UiWindow(); root.AddChild(second);
            var round=new UiRoundFrame(); workbench.AddChild(round); round.Content.AddChild(new ColorRect { Color=Colors.White }); await Frames(2);
            Check(modal.Open() && GameUi.GameplayModalOpen,"Modal did not register."); Check(!second.Open(),"Two modals were opened.");
            var activated=false; var action=GameUi.Button("Keyboard action",()=>activated=true); modal.Body.AddChild(action); await Frames(2); action.GrabFocus();
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode=Key.Enter,Keycode=Key.Enter,Pressed=true }); await Frames(1);
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode=Key.Enter,Keycode=Key.Enter,Pressed=false }); await Frames(1);
            Check(activated,"Modal swallowed keyboard button activation.");
            modal.Close(); Check(!GameUi.GameplayModalOpen && GetViewport().GuiGetFocusOwner()==original,"Close did not restore focus/state.");
            modal.Open(); Input.ParseInputEvent(new InputEventKey { PhysicalKeycode=Key.Escape,Keycode=Key.Escape,Pressed=true }); await Frames(2);
            Check(!modal.Visible && !GameUi.GameplayModalOpen,"Escape did not close modal.");
            modal.Open(); modal.QueueFree(); await Frames(2); Check(!GameUi.GameplayModalOpen,"Freed modal leaked gameplay lock.");
            root.QueueFree(); workbench.QueueFree(); await Frames(3);
            GD.Print("UI_FOUNDATION_OK: catalog validation, fallbacks, cached themes, eight layouts, resize, isolated model, modal exclusivity, Escape, focus and teardown."); GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    private async Task Frames(int count) { for(var i=0;i<count;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string name)
    {
        if(!OS.GetCmdlineUserArgs().Contains("--capture")) return;
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/ui-foundation/"+name+".png");
    }
}
