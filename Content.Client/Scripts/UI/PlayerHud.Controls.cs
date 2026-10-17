using Content.Shared.Combat;
using Content.Shared.Movement;
using Content.Shared.Network;
using Godot;
using ProjectG.Combat;

namespace ProjectG.UI;

public partial class PlayerHud
{
    private PanelContainer _controlPanel=null!;
    private readonly Label[] _controlRows=new Label[NetworkConstants.MaxControlKinds];
    private ControlState? _controls;
    private double _controlsReceived;

    private void InitializeControls(Control root)
    {
        _controlPanel=new PanelContainer {Name="ControlStatusPanel",AnchorLeft=1,AnchorRight=1,
            OffsetLeft=-324,OffsetRight=-16,OffsetTop=80,OffsetBottom=360,Visible=false};
        root.AddChild(_controlPanel);
        var column=new VBoxContainer();_controlPanel.AddChild(column);column.AddChild(GameUi.Text("Контроль",18));
        var scroll=new ScrollContainer {SizeFlagsVertical=Control.SizeFlags.ExpandFill,
            HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};column.AddChild(scroll);
        var rows=new VBoxContainer {SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};scroll.AddChild(rows);
        for(var i=0;i<_controlRows.Length;i++)
        {
            var label=GameUi.Text("",14);label.Name=$"ControlStatus_{(CrowdControlKind)i}";
            label.AutowrapMode=TextServer.AutowrapMode.WordSmart;label.Visible=false;rows.AddChild(label);_controlRows[i]=label;
        }
    }

    public void Apply(ControlState state)
    {
        if(state.OwnerId!=_owner || _controls is {} previous && !MovementSimulation.IsSequenceNewer(state.ServerTick,previous.ServerTick))return;
        _controls=state;_controlsReceived=Time.GetTicksMsec()/1000d;UpdateControls();
    }

    private void UpdateControls()
    {
        foreach(var row in _controlRows)row.Visible=false;
        var visible=false;
        if(_controls is {} state)
        {
            var elapsed=Time.GetTicksMsec()/1000d-_controlsReceived;
            foreach(var control in state.Controls)
            {
                var remaining=Math.Max(0,control.RemainingSeconds-elapsed);if(remaining<=0)continue;
                var row=_controlRows[(int)control.Kind];row.Visible=true;visible=true;
                var instances=control.Instances>1?$" · {control.Instances} эффекта":"";
                var magnitude=control.Kind switch
                {
                    CrowdControlKind.Slow=>$" · суммарная сила {control.Strength*100:0.#}%",
                    CrowdControlKind.Blind=>$" · −{control.Strength:0.#} Accuracy",
                    CrowdControlKind.Taunt=>$" · −{control.Strength*100:0.#}% урона другим целям",
                    _=>""
                };
                var name=control.Kind==CrowdControlKind.Knockup?"Подброс и падение":CombatJournalPresentation.ControlName(control.Kind);
                row.Text=$"{name} · до {remaining:0.0} с{instances}{magnitude}\n{ControlRestriction(control.Kind)}";
                row.TooltipText="Показано оставшееся время последнего из действующих эффектов этого вида. Подходящее очищение снимает только разрешённые ему виды контроля.";
            }
        }
        _controlPanel.Visible=visible;
    }

    private string ControlRestriction(CrowdControlKind kind)=>kind switch
    {
        CrowdControlKind.Root=>"Обычное движение и движения навыков недоступны.",
        CrowdControlKind.Slow=>"Ограничивает скорость передвижения до установленного предела.",
        CrowdControlKind.Silence=>"Магические действия недоступны.",
        CrowdControlKind.Disarm=>"Действия с требованием оружия недоступны.",
        CrowdControlKind.Blind=>"Снижена числовая точность попадания.",
        CrowdControlKind.Taunt=>"Можно выбирать цель и управлять персонажем.",
        CrowdControlKind.Knockback or CrowdControlKind.Pull=>"Принудительное перемещение; обычные действия недоступны.",
        CrowdControlKind.Knockup or CrowdControlKind.Knockdown=>QuickRecoverHint(),
        CrowdControlKind.Sleep=>"Обычные действия недоступны. Потеря HP пробуждает.",
        CrowdControlKind.Fear=>"Персонаж убегает от источника. Камера доступна.",
        _=>"Обычные движение, атаки, навыки и защита недоступны."
    };

    private string QuickRecoverHint()
    {
        var cooldown=Math.Max(0,_defense.QuickRecoverCooldown-(Time.GetTicksMsec()/1000d-_defenseReceived));
        return cooldown>0 ? $"Quick Recover · ещё {cooldown:0.0} с" : _defense.Stamina<_defense.QuickRecoverCost
            ? $"Quick Recover · нужно {_defense.QuickRecoverCost:0} выносливости" : "Space: удержать для прицела, отпустить для Quick Recover.";
    }
}
