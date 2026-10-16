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
            await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
            if(actor.Visible)throw new Exception("Expired corpse remained visible.");
            life=life with {ServerTick=2,CorpseRemaining=0,RespawnRemaining=0};ui.Apply(life);await Frames(2);
            if(button.Disabled)throw new Exception("Respawn stayed disabled after its server timer.");
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
            var journal=new CombatJournalPresentation();AddChild(journal);journal.Initialize(network,new(1));journal.Toggle();
            journal.Apply(new CombatJournalIndex(new(1),3,[new(1,100000,2)]));
            var entry=new CombatJournalEntry(1,100000,CombatJournalKind.Damage,"Монстр #2","Вы",10);
            journal.Apply(new CombatJournalChunk(new(1),0,0,0,0,[entry]));await Frames(3);
            var journalText=journal.FindChildren("CombatJournalText","Label",true,false).OfType<Label>().Single();
            if(!journalText.Text.Contains("Монстр #2") || !journalText.Text.Contains("−10"))throw new Exception("Current combat journal did not display server evidence.");
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
            if(!progression.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text.Contains("Пассивный")) ||
                progression.FindChildren("*","OptionButton",true,false).Any())throw new Exception("Passive UI exposed an active-slot assignment or lost its passive explanation.");
            progression.QueueFree();await Frames(2);
            var hud=new ProjectG.UI.PlayerHud();AddChild(hud);hud.Initialize(new(1),()=>{});
            hud.Apply(new ProgressionState(new(1),1,1,0,100,0,0,[10,10,10,10,10,10,10],[new(606,1,0,2,1,false)]));
            hud.Apply(new AbilityLoadout(new(1),1,100,140,[new(606,AbilityForm.GroundArea,6,1,0,0,5,2,0,
                Availability:AbilityAvailability.NeedsWeapon,Area:new(AbilityAreaShape.Circle,1),ExecutionForm:CombatActionForm.Toggle)]));
            await Frames(2);
            if(!hud.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text=="Нужно оружие"))
                throw new Exception("Weapon-dependent Toggle was displayed as a Parry requirement.");
            hud.QueueFree();await Frames(2);
            ui.QueueFree();actor.QueueFree();await Frames(2);
            network.Free();
            GD.Print("LIFE_UI_OK: death window/timers, respawn, healing/resurrection feedback, private journal, orphan projectile, passive skill without active-slot controls, duplicate/foreign filtering and cleanup.");GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private async Task Frames(int count){for(var i=0;i<count;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
}
