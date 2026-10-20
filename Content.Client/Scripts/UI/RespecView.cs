using System.Text;
using Content.Shared.Network;
using Godot;
using ProjectG.Progression;
namespace ProjectG.UI;

/// <summary>Pure editable view; the presenter supplies quotes and server outcomes.</summary>
public partial class RespecView:VBoxContainer
{
    private Label _summary=null!,_status=null!;
    private RichTextLabel _comparison=null!;
    private CheckBox _ack=null!;
    private Button _confirm=null!,_refresh=null!,_allocate=null!,_close=null!;
    private bool _ready;
    public event Action? ConfirmRequested,RefreshRequested,CloseRequested,AllocateRequested;
    public override void _Ready()
    {
        _summary=GetNode<Label>("%Summary");_status=GetNode<Label>("%Status");_comparison=GetNode<RichTextLabel>("%Comparison");
        _ack=GetNode<CheckBox>("%Acknowledge");_confirm=GetNode<Button>("%Confirm");_refresh=GetNode<Button>("%Refresh");
        _allocate=GetNode<Button>("%Allocate");_close=GetNode<Button>("%Close");
        _ack.Toggled+=Acknowledged;_confirm.Pressed+=Confirm;_refresh.Pressed+=Refresh;_close.Pressed+=Close;_allocate.Pressed+=Allocate;
    }
    public void SetQuote(RespecQuote? quote)
    {
        _ack.ButtonPressed=false;
        if(quote is not {} q){_comparison.Text="Получите актуальный расчёт перед подтверждением.";_summary.Text="Полный сброс характеристик";return;}
        _summary.Text=$"Возврат: {q.ReturnedPoints} · Свободных после сброса: {q.AvailableAfter}\n"+
            (q.FirstFree?"Первый сброс — бесплатно":$"Стоимость: {q.Price} монет · В кошельке: {q.Coins}");
        var text=new StringBuilder("[b]Характеристики: сейчас → после сброса[/b]\n");
        for(var i=0;i<7;i++)text.AppendLine($"{ProgressionPresentation.PrimaryStatName(i)}: {q.PrimaryBefore[i]:0} → {q.PrimaryAfter[i]:0}");
        text.AppendLine("\n[b]Производные показатели[/b]");
        for(var i=0;i<q.Current.Length;i++)text.AppendLine($"{ProgressionPresentation.DerivedStatName(i)}: {ProgressionPresentation.Format(i,q.Current[i])} → {ProgressionPresentation.Format(i,q.Projected[i])}");
        _comparison.Text=text.ToString();
    }
    public void Render(bool ready,bool pending,bool completed,string status,int price=0)
    {
        _ready=ready&&!pending;_status.Text=status;_ack.Disabled=!_ready;
        _confirm.Disabled=!_ready||!_ack.ButtonPressed;_refresh.Disabled=pending;_allocate.Visible=completed;
        _confirm.Visible=!completed;_confirm.Text=price==0?"Сбросить бесплатно":$"Сбросить за {price} монет";
    }
    private void Acknowledged(bool value)=>_confirm.Disabled=!_ready||!value;
    private void Confirm()=>ConfirmRequested?.Invoke();private void Refresh()=>RefreshRequested?.Invoke();
    private void Close()=>CloseRequested?.Invoke();private void Allocate()=>AllocateRequested?.Invoke();
    public override void _ExitTree()
    {if(_ack is null)return;_ack.Toggled-=Acknowledged;_confirm.Pressed-=Confirm;_refresh.Pressed-=Refresh;_close.Pressed-=Close;_allocate.Pressed-=Allocate;}
}
