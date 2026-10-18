using Content.Shared.Network;
using Content.Shared.Combat;
using Godot;
using ProjectG.Combat;
using ProjectG.Networking;
namespace ProjectG.Tests.UI;

public partial class LifeSmoke:Node
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Size=new(1280,720);
            using var network=new NetworkClient(); // DTO presentation fixture; no identity or transport is opened.
            var ui=new LifePresentation();AddChild(ui);ui.Initialize(network,new(1));
            var actor=new Node3D();AddChild(actor);var presentation=new CombatPresentation();actor.AddChild(presentation);
            presentation.Initialize(new(new(1),1,CombatEntityKind.Player,default,0,100,1,2,1),network,false);
            var life=new ActorLifeState(new(1),1,1,true,.1,60);ui.Apply(life);presentation.ApplyLife(life);
            await Frames(3);var button=ui.FindChildren("RespawnButton","Button",true,false).OfType<Button>().Single();
            if(!button.Disabled)throw new Exception("Respawn was enabled before its server timer.");
            var window=ui.GetChildren().OfType<ProjectG.UI.UiWindow>().Single();
            if(!window.Visible)throw new Exception("The death window did not open without a PvP panel.");
            await UiReviewEvidence.Capture(this,"death-wait");
            await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
            if(actor.Visible)throw new Exception("Expired corpse remained visible.");
            life=life with {ServerTick=2,CorpseRemaining=0,RespawnRemaining=0};ui.Apply(life);await Frames(2);
            if(button.Disabled)throw new Exception("Respawn stayed disabled after its server timer.");
            await UiReviewEvidence.Capture(this,"death-ready");
            life=life with {ServerTick=3,Dead=false};ui.Apply(life);presentation.ApplyLife(life);await Frames(2);
            if(!actor.Visible || window.Visible)throw new Exception("Revived actor/window did not return to live presentation.");
            presentation.ApplyState(new(new(1),3,CombatEntityKind.Player,default,10,100,1,2,1));
            presentation.ApplyFeedback(new(1,7,new(2),new(1),3,CombatFeedbackKind.Resurrection,10,false));
            var feedback= presentation.GetChildren().OfType<Label3D>().Single();
            if(!feedback.Text.Contains("ВОСКРЕШЕНИЕ +10 HP"))throw new Exception("Resurrection feedback was lost.");
            presentation.ApplyFeedback(new(2,8,new(2),new(1),3,CombatFeedbackKind.Healing,20,true));
            if(!feedback.Text.Contains("CRIT +20 HP"))throw new Exception("Healing was not shown as positive critical HP.");
            presentation.ApplyFeedback(new(1,7,new(2),new(1),3,CombatFeedbackKind.Resurrection,10,false));
            if(!feedback.Text.Contains("CRIT +20 HP"))throw new Exception("A repeated effect event overwrote newer feedback.");
            presentation.ApplyFeedback(new(3,9,new(2),new(99),3,CombatFeedbackKind.Healing,40,false));
            if(!feedback.Text.Contains("CRIT +20 HP") || presentation.Health!=10)throw new Exception("Feedback changed authoritative HP or another target.");
            presentation.ApplyControlFeedback(new(1,new(2),new(1),3,CrowdControlKind.Root,ControlChangeKind.Applied,2,1,1));
            if(!feedback.Text.Contains("КОНТРОЛЬ: Обездвиживание") || !feedback.Text.Contains("CRIT +20 HP") || presentation.Health!=10)
                throw new Exception("Control confirmation overwrote health or damage/healing feedback.");
            presentation.ApplyControlFeedback(new(2,new(2),new(1),3,CrowdControlKind.Root,ControlChangeKind.Expired,0,0,1));
            if(!feedback.Text.Contains("ИСТЁК: Обездвиживание") || !feedback.Text.Contains("ещё действует: 1"))
                throw new Exception("One expired source falsely cleared overlapping control feedback.");
            presentation.ApplyControlFeedback(new(1,new(2),new(1),3,CrowdControlKind.Root,ControlChangeKind.Applied,2,1,1));
            presentation.ApplyControlFeedback(new(3,new(2),new(99),3,CrowdControlKind.Stun,ControlChangeKind.Applied,2,1,1));
            if(!feedback.Text.Contains("ИСТЁК: Обездвиживание"))throw new Exception("Stale or foreign control feedback reached the actor.");
            presentation.ApplyControlFeedback(new(3,new(2),new(1),3,CrowdControlKind.Root,ControlChangeKind.Removed,0,0,0));
            if(!feedback.Text.Contains("СНЯТ: Обездвиживание") || feedback.Text.Contains("ещё действует"))throw new Exception("Final control removal was not confirmed.");
            presentation.ApplyControlFeedback(new(4,new(2),new(1),3,CrowdControlKind.Stun,ControlChangeKind.Rejected,0,0,0,ControlMitigation.Immunity));
            if(!feedback.Text.Contains("ИММУНИТЕТ: Оглушение") || presentation.Health!=10)throw new Exception("Rejected control invented damage or a successful application.");
            presentation.ApplyControlFeedback(new(5,new(2),new(1),3,CrowdControlKind.Root,ControlChangeKind.Rejected,0,0,0,ControlMitigation.AdaptiveResistance));
            if(!feedback.Text.Contains("СОПРОТИВЛЕНИЕ") || !feedback.Text.Contains("устойчивость к повторному контролю"))throw new Exception("ACR rejection reason was lost.");
            presentation.ApplyControlFeedback(new(6,new(2),new(1),3,CrowdControlKind.Root,ControlChangeKind.Applied,1,1,1,ControlMitigation.Resistance));
            if(!feedback.Text.Contains("ослаблен") || !feedback.Text.Contains("КОНТРОЛЬ"))throw new Exception("Partially resisted application was shown as immunity.");
            presentation.ApplyAbilityDamage(new(9,new(2),new(1),3,0,10,GuardImpact.Dodged));
            if(!feedback.Text.Contains("УВОРОТ") || presentation.Health!=10)throw new Exception("Confirmed Dodge was displayed as damage or numeric evasion.");
            presentation.ApplyFeedback(new(4,10,new(2),new(1),3,CombatFeedbackKind.Dodge,5,false));
            if(!feedback.Text.Contains("УВОРОТ") || presentation.Health!=10)throw new Exception("Periodic Dodge confirmation changed HP.");
            presentation.ApplyDamage(new(new(2),1,3,default,System.Numerics.Vector2.UnitY,2,new(1),2,8,false,
                ExecutionId:1,Tags:CombatHitTags.BackHit|CombatHitTags.ProneHit));
            if(!feedback.Text.Contains("со спины") || !feedback.Text.Contains("по лежащей цели") || presentation.Health!=8)
                throw new Exception("Basic contact context was not shown with its confirmed HP.");
            presentation.ApplyAbilityDamage(new(2,new(2),new(1),3,1,7,Tags:CombatHitTags.FrontHit|CombatHitTags.AirborneHit));
            if(!feedback.Text.Contains("спереди") || !feedback.Text.Contains("в воздухе") || feedback.Text.Contains("со спины"))
                throw new Exception("Ability contact context was lost or retained previous tags.");
            var journal=new CombatJournalPresentation();AddChild(journal);journal.Initialize(network,new(1));journal.Toggle();
            journal.Apply(new CombatJournalIndex(new(1),3,[new(1,100000,2)]));
            var entry=new CombatJournalEntry(1,100000,CombatJournalKind.Damage,"Монстр #2","Вы",10,HitTags:CombatHitTags.BackHit|CombatHitTags.ProneHit);
            journal.Apply(new CombatJournalChunk(new(1),0,0,0,0,[entry]));await Frames(3);
            var journalText=journal.FindChildren("CombatJournalText","Label",true,false).OfType<Label>().Single();
            if(!journalText.Text.Contains("Монстр #2") || !journalText.Text.Contains("−10") || !journalText.Text.Contains("со спины") || !journalText.Text.Contains("по лежащей цели"))
                throw new Exception("Current combat journal did not display server evidence and contact context.");
            journal.Apply(new CombatJournalChunk(new(1),0,0,0,0,[entry with {Id=2,Kind=CombatJournalKind.ControlRemoved,Control=CrowdControlKind.Freeze,Amount=1},
                entry with {Id=3,Kind=CombatJournalKind.EffectsDispelled,Amount=2}]));
            await ToSignal(GetTree().CreateTimer(.25),SceneTreeTimer.SignalName.Timeout);
            if(!journalText.Text.Contains("Снят контроль: Заморозка") || !journalText.Text.Contains("Рассеяно положительных эффектов: 2"))
                throw new Exception("Cleanse/Dispel feedback was displayed as damage or lost.");
            journal.Select(1);journal.Apply(new CombatJournalChunk(new(1),2,1,0,2,[entry,entry with {Id=2,Kind=CombatJournalKind.Death}]));
            await ToSignal(GetTree().CreateTimer(.25),SceneTreeTimer.SignalName.Timeout);
            if(!journalText.Text.Contains("СМЕРТЬ"))throw new Exception("Selected death report did not assemble/display.");
            journal.Apply(new CombatJournalChunk(new(99),0,0,0,0,[entry with {Id=3,Source="Чужой журнал"}]));await Frames(2);
            if(journalText.Text.Contains("Чужой"))throw new Exception("Foreign journal reached the owner UI.");
            journal.QueueFree();await Frames(2);
            var world=new ProjectG.Gameplay.WorldController{AutoConnect=false};
            var applyEffect=typeof(ProjectG.Gameplay.WorldController).GetMethod("OnAbilityEffect",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var flying=new AbilityEffectState(1,new(99),1,3,1,AbilityForm.Projectile,AbilityPhase.Flying,default,default,
                System.Numerics.Vector2.UnitX,.1f,2,2);
            applyEffect.Invoke(world,[flying]);var projectile=world.GetChildren().OfType<AbilityEffectVisual>().Single();
            projectile._Ready(); // Parentless DTO fixture: attach its owned mesh exactly as scene entry does.
            if(projectile.Caster is not null)throw new Exception("A released projectile invented an absent caster.");
            var from=projectile.Position;projectile._Process(.1);
            if(projectile.Position.X<=from.X)throw new Exception("An orphan projectile stopped its visual flight.");
            applyEffect.Invoke(world,[flying with {Phase=AbilityPhase.Finished}]);await Frames(2);
            if(world.GetChildren().OfType<AbilityEffectVisual>().Any())throw new Exception("Orphan projectile did not finish.");
            world.Free();
            var progression=new ProjectG.Progression.ProgressionPresentation();AddChild(progression);progression.Initialize(network);
            progression.Apply(new(new(1),1,1,0,100,0,0,[10,10,10,10,10,10,10],[new(610,1,0,2,0,false,true)]));await Frames(2);
            var statLabels=progression.FindChildren("*","Label",true,false).OfType<Label>().ToArray();
            if(!statLabels.Any(l=>l.Text.StartsWith("Макс. выносливость:")) || !statLabels.Any(l=>l.Text.StartsWith("Реген выносливости /с:")) ||
                statLabels.Any(l=>l.Text.Contains("реген HP пока не работает")))throw new Exception("Character resource rows are stale or incomplete.");
            if(!progression.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text.Contains("Пассивный")) ||
                progression.FindChildren("*","OptionButton",true,false).OfType<OptionButton>().Any(b=>b.Visible))throw new Exception("Passive UI exposed an active-slot assignment or lost its passive explanation.");
            progression.QueueFree();await Frames(2);
            var hud=new ProjectG.UI.PlayerHud();AddChild(hud);hud.Initialize(new(1),()=>{});
            hud.Apply(new ProgressionState(new(1),1,1,0,100,0,0,[10,10,10,10,10,10,10],[new(606,1,0,2,1,false)]));
            hud.Apply(new AbilityLoadout(new(1),1,100,140,[new(606,AbilityForm.GroundArea,6,1,0,0,5,2,0,
                Availability:AbilityAvailability.NeedsWeapon,Area:new(AbilityAreaShape.Circle,1),ExecutionForm:CombatActionForm.Toggle)]));
            await Frames(2);
            if(!hud.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text=="Нужно оружие"))
                throw new Exception("Weapon-dependent Toggle was displayed as a Parry requirement.");
            hud.Apply(new AbilityLoadout(new(1),2,0,140,[new(3,AbilityForm.Dash,3,.45f,15,0,3,0,0,20,
                ExecutionForm:CombatActionForm.Instant,
                DodgeDurationSeconds:.2,DodgeProtectionSeconds:.15,DodgeProtectedThreats:DodgeThreats.All)]));await Frames(2);
            if(!hud.FindChildren("*","Control",true,false).OfType<Control>().Any(c=>c.TooltipText.Contains("Защита:")&&c.TooltipText.Contains("периодический магический урон")&&!c.TooltipText.Contains("без неуязвимости")))
                throw new Exception("Dodge HUD lost its approved protection policy.");
            hud.Apply(new DefenseState(new(1),9,0,DefenseOutcome.NoStamina,0,0,.8,0,0,20,10,false,1,System.Numerics.Vector2.UnitY));await Frames(2);
            if(hud.FindChildren("StaminaText","Label",true,false).OfType<Label>().Single().Text!="0 / 0")
                throw new Exception("Zero-capacity stamina HUD invented a resource balance.");
            hud.Apply(new DefenseState(new(1),10,0,DefenseOutcome.Accepted,100,100,.8,0,0,20,10,false,1,System.Numerics.Vector2.UnitY));
            hud.Apply(new ControlState(new(1),10,[new(CrowdControlKind.Root,.25,1,2),new(CrowdControlKind.Slow,2,.5,2),
                new(CrowdControlKind.Knockdown,2,1,1),new(CrowdControlKind.Blind,2,15,1),new(CrowdControlKind.Taunt,2,.3,1)]));await Frames(2);
            var controlPanel=hud.FindChildren("ControlStatusPanel","PanelContainer",true,false).OfType<PanelContainer>().Single();
            var controlRows=hud.FindChildren("ControlStatus_*","Label",true,false).OfType<Label>().ToDictionary(l=>l.Name.ToString());
            if(!controlPanel.Visible || !controlRows["ControlStatus_Root"].Text.Contains("2 эффекта") ||
                !controlRows["ControlStatus_Knockdown"].Text.Contains("Quick Recover") || !controlRows["ControlStatus_Blind"].Text.Contains("15 Accuracy") ||
                !controlRows["ControlStatus_Taunt"].Text.Contains("30%"))throw new Exception("Control timers lost overlaps, strength or escape instructions.");
            hud.Apply(new ControlState(new(99),11,[]));hud.Apply(new ControlState(new(1),9,[]));
            if(!controlPanel.Visible)throw new Exception("Foreign or stale control state cleared the HUD.");
            await ToSignal(GetTree().CreateTimer(.35),SceneTreeTimer.SignalName.Timeout);
            if(controlRows["ControlStatus_Root"].Visible || !controlRows["ControlStatus_Slow"].Visible)
                throw new Exception("Control countdown erased other live controls or kept an expired row.");
            hud.Apply(new ControlState(new(1),12,[]));
            if(controlPanel.Visible)throw new Exception("Confirmed control removal did not clear its panel.");
            hud.QueueFree();await Frames(2);
            await CheckEcho(network);
            ui.QueueFree();actor.QueueFree();await Frames(2);
            network.Free();
            UiReviewEvidence.Record("life-and-echo","PASS: presentation DTO fixtures, death timers, respawn, journal, passive skill, owned Echo and refusal");
            GD.Print("LIFE_UI_OK: death window/timers, respawn, healing/resurrection feedback, private journal, orphan projectile, passive skill without active-slot controls, duplicate/foreign filtering and cleanup.");GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private async Task Frames(int count){for(var i=0;i<count;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task CheckEcho(NetworkClient network)
    {
        var controls=new ProjectG.Echoes.EchoControls();AddChild(controls);controls.Initialize(network,new(1));
        var apply=typeof(ProjectG.Echoes.EchoControls).GetMethod("ApplyRoster",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var row=new EchoRosterEntry(100,"Мира",EchoAvailability.Ready,0,default,110,140,100);
        apply.Invoke(controls,[new EchoRosterPage(new(1),1,0,0,2,[row,row with {Handle=101,Name="Другое Эхо",Availability=EchoAvailability.RequiresRestoration,Health=0,DeathId=1}])]);
        controls.Toggle();await Frames(2);
        await UiReviewEvidence.Capture(this,"echo-owned");
        var buttons=controls.FindChildren("*","Button",true,false).OfType<Button>().ToArray();
        if(buttons.Single(b=>b.Text.StartsWith("Призвать")).Disabled || !buttons.Any(b=>b.Text.Contains("Требует восстановления")))
            throw new Exception("Owned Echo UI hid Ready/dead records or disabled a valid summon.");
        apply.Invoke(controls,[new EchoRosterPage(new(1),2,0,0,1,[row with {Availability=EchoAvailability.RequiresRestoration,Health=0,DeathId=1}])]);
        await Frames(2);
        if(!controls.FindChildren("*","Button",true,false).OfType<Button>().Any(b=>b.Text.StartsWith("Восстановить")&&!b.Disabled))
            throw new Exception("Dead Echo had no explicit seal recovery control.");
        apply.Invoke(controls,[new EchoRosterPage(new(1),2,0,0,1,[row with {Availability=EchoAvailability.Summoning,ActionSequence=7,Execution=19}])]);
        await Frames(2);
        if(!controls.FindChildren("*","Button",true,false).OfType<Button>().Any(b=>b.Text=="Отменить призыв"&&!b.Disabled))
            throw new Exception("Confirmed summon had no execution-specific cancel control.");
        var echo=new ProjectG.Echoes.EchoPresentation();AddChild(echo);
        var grid=new Content.Shared.Navigation.NavigationGrid(new(new(-15,-15),1,.25f,30,30,new byte[900]));
        var spawn=new EchoSpawn(new(11),new(1),1,1,new(-7,2),"Мира");echo.Initialize(spawn,grid);
        var state=new CombatState(spawn.EntityId,1,CombatEntityKind.Echo,spawn.Position,110,110,1,2,Mathf.Pi/4,
            Body:new(CombatBodyShape.Capsule,2,.4f),BasicOriginHeight:1,BasicThickness:.3f);
        echo.BindCombat(state,grid);var combat=new CombatPresentation();echo.AddChild(combat);combat.Initialize(state,network,false);
        echo.Animation.Basic(new(spawn.EntityId,1,1,2,CombatActionPhase.Preparation,spawn.Position,System.Numerics.Vector2.UnitY,0,2,Mathf.Pi/4,.2,.1,.7,.2,1,true,ActionCancelPermission.Voluntary));
        var mesh=echo.PreviewBody;controls.Observe(spawn,mesh.Mesh,mesh.MaterialOverride);
        apply.Invoke(controls,[new EchoRosterPage(new(1),2,0,0,1,[row with {Availability=EchoAvailability.Active,Slot=1,EntityId=spawn.EntityId}])]);
        await Frames(2);
        buttons=controls.FindChildren("*","Button",true,false).OfType<Button>().ToArray();
        if(!buttons.Single(b=>b.Text.StartsWith("Призвать")).Disabled || !buttons.Any(b=>b.Text.StartsWith("Отозвать")) || !echo.Animation.IsAnimating || combat.Health!=110)
            throw new Exception("Echo active UI, common HP or basic animation did not follow server state.");
        controls.Apply(new(new(1),3,[new(1,spawn.EntityId,6,2,0,true)]));
        apply.Invoke(controls,[new EchoRosterPage(new(1),3,0,0,1,[row with {Availability=EchoAvailability.Active,Slot=1,EntityId=spawn.EntityId,Refusal=EchoRefusal.Basic|EchoRefusal.Signature}])]);
        await Frames(2);
        if(!controls.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text=="Отказывается применять способность") ||
            !controls.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text.Contains("отказывается от обычных атак")))
            throw new Exception("Echo refusal was shown as Ready or its public decision was lost.");
        echo.Animation.SetAlive(false);combat.ApplyState(state with {ServerTick=3,Health=0});await Frames(2);
        if(combat.IsAlive || echo.Animation.IsAnimating)throw new Exception("Echo death left its common presentation alive.");
        controls.QueueFree();echo.QueueFree();await Frames(2);
        GD.Print("ECHO_UI_OK: owned Ready/dead rows, explicit summon/recall controls, common combat body/HP/animation and cleanup.");
    }
}
