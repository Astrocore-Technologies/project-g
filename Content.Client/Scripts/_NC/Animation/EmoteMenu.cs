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
        var root=new Control(); AddChild(root);
        _window=new UiWindow { ToggleKey=Key.F4 }; root.AddChild(_window);
        _window.Build("Эмоции · F4",new(480,510)); _window.CloseRequested+=_window.Close;
        (AvatarGesture Gesture,string Label)[] entries=[(AvatarGesture.Wave,"Помахать"),(AvatarGesture.Nod,"Кивнуть"),
            (AvatarGesture.No,"Отказаться"),(AvatarGesture.Bow,"Поклониться"),(AvatarGesture.Point,"Указать"),
            (AvatarGesture.Cheer,"Порадоваться"),(AvatarGesture.Clap,"Поаплодировать"),(AvatarGesture.Sit,"Сесть"),(AvatarGesture.None,"Закончить / встать")];
        foreach(var entry in entries) _window.Body.AddChild(GameUi.Button(entry.Label,()=>Send(entry.Gesture)));
        _window.Body.AddChild(GameUi.Text("Движение и бой прерывают эмоцию.",14));
        GameUi.RegisterRoute("emotes",()=>_window.Open());
    }
    private void Send(AvatarGesture gesture)
    {
        if(!_owner.IsAlive)return;
        _owner.StopMovement(); _owner.GetNodeOrNull<ProjectG.Combat.CombatPresentation>("CombatPresentation")?.CancelAutoAttack();
        if(++_sequence==0)++_sequence;
        _network.SendEmote(new(_sequence,gesture)); _window.Close();
    }
    public override void _UnhandledInput(InputEvent ev)
    {
        if(ev is InputEventKey { Pressed:true,Echo:false,PhysicalKeycode:Key.F4 } && !_window.Visible && _owner.IsAlive)
        { _window.Open();GetViewport().SetInputAsHandled(); }
    }
}
