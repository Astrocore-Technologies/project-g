using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Animation;

/// <summary>Social gestures are cancellable cosmetic intentions, never movement locks.</summary>
public partial class EmoteMenu : CanvasLayer
{
    private NetworkClient _network=null!;
    private PlayerController _owner=null!;
    private UiWindow _window=null!;
    private uint _sequence;
    public void Initialize(NetworkClient network, PlayerController owner)
    {
        _network=network; _owner=owner;
        var root=new Control { MouseFilter=Control.MouseFilterEnum.Ignore }; AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _window=new UiWindow { ToggleKey=Key.F4 }; root.AddChild(_window);
        _window.Build("Эмоции",new(840,480)); _window.CloseRequested+=_window.Close;
        var view=GD.Load<PackedScene>("res://UI/Screens/EmoteView.tscn").Instantiate<Control>();_window.Body.AddChild(view);
        (AvatarGesture Gesture,string Label)[] entries=[(AvatarGesture.Wave,"Помахать"),(AvatarGesture.Nod,"Кивнуть"),
            (AvatarGesture.No,"Отказаться"),(AvatarGesture.Bow,"Поклониться"),(AvatarGesture.Point,"Указать"),
            (AvatarGesture.Cheer,"Порадоваться"),(AvatarGesture.Clap,"Поаплодировать"),(AvatarGesture.Sit,"Сесть"),(AvatarGesture.None,"Закончить / встать")];
        foreach(var entry in entries) { var gesture=entry.Gesture; view.GetNode<Button>("%"+gesture).Pressed+=()=>Send(gesture); }
        GameUi.RegisterRoute("emotes",()=>_window.Open());
    }
    private void Send(AvatarGesture gesture)
    {
        if(!_owner.IsAlive)return;
        _owner.StopMovement(); _owner.GetNodeOrNull<ProjectG.Combat.CombatPresentation>("CombatPresentation")?.CancelBasicAttack();
        if(++_sequence==0)++_sequence;
        _network.SendEmote(new(_sequence,gesture)); _window.Close();
    }
    public override void _UnhandledInput(InputEvent ev)
    {
        if(PcInput.Pressed("emote",ev) && !_window.Visible && _owner.IsAlive)
        { _window.Open();GetViewport().SetInputAsHandled(); }
    }
}
