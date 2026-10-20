using Content.Shared.Network;
using Godot;

namespace ProjectG.UI;

/// <summary>Editable view shared by the live NPC window and standalone preview. Owns no game state.</summary>
public partial class TavernView:VBoxContainer
{
    private Label _selected=null!,_home=null!,_status=null!;
    private Button _bind=null!;
    public event Action? BindRequested,CloseRequested,RespecRequested;
    public override void _Ready()
    {
        _selected=GetNode<Label>("%SelectedInn");_home=GetNode<Label>("%CurrentHome");_status=GetNode<Label>("%Status");
        _bind=GetNode<Button>("%Bind");_bind.Pressed+=Bind;
        GetNode<Button>("%Close").Pressed+=Close;
        GetNode<Button>("%Respec").Pressed+=Respec;
    }
    private void Bind()=>BindRequested?.Invoke();
    private void Close()=>CloseRequested?.Invoke();
    private void Respec()=>RespecRequested?.Invoke();
    public void Render(string selected,TavernState? home,bool alive,bool inCombat,bool pending,string status)
    {
        _selected.Text=selected;
        _home.Text=home is {} state?"Ваше место возрождения: "+(state.TavernId==0?"не назначено":state.TavernName):"Получение места возрождения…";
        _status.Text=pending?"Сохраняем новое место возвращения…":!alive?"Услуга доступна живому персонажу.":inCombat?"Сначала выйдите из боя.":status;
        _bind.Disabled=home is null||!alive||inCombat||pending;
    }
    public override void _ExitTree()
    {if(_bind is not null){_bind.Pressed-=Bind;GetNode<Button>("%Close").Pressed-=Close;GetNode<Button>("%Respec").Pressed-=Respec;}}
}
