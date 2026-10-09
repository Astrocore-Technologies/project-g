using Content.Shared.Network;
using Godot;
using ProjectG.Combat;
using V2 = System.Numerics.Vector2;

namespace ProjectG.Tests.Combat;

/// <summary>Headless assertions and a rendered contact-pose sheet for every editable sword clip.</summary>
public partial class SwordAnimationSmoke : Node3D
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Size = new(1440,900);
            AddChild(new WorldEnvironment { Environment = new Godot.Environment {
                BackgroundMode=Godot.Environment.BGMode.Color, BackgroundColor=new("203442"),
                AmbientLightSource=Godot.Environment.AmbientSource.Color, AmbientLightColor=Colors.White, AmbientLightEnergy=.7f } });
            AddChild(new DirectionalLight3D { RotationDegrees=new(-50,-25,0),LightEnergy=1.4f });
            var camera=new Camera3D {Position=new(0,18,18),Current=true,Fov=53}; AddChild(camera); camera.LookAt(Vector3.Zero);
            AddChild(new MeshInstance3D { Mesh=new PlaneMesh {Size=new(24,23)},Position=new(0,-1.02f,0),
                MaterialOverride=new StandardMaterial3D {AlbedoColor=new("738779")} });
            var packed=GD.Load<PackedScene>("res://Scenes/Actors/SwordAttackRig.tscn");
            var names=new[]{"Обычный →","Обычный ←","Выпад","Размашистый","Рассечение","Проламывающий","Навершием","Подрез","Ответный","Круговой","Передышка","Решающий","Рывок"};
            ushort[] ids=[0,0,20,21,22,23,24,25,26,27,28,29,3];
            var rigs=new List<SwordAttackAnimation>();
            for(var i=0;i<ids.Length;i++)
            {
                var actor=new CharacterBody3D {Position=new((i%4-1.5f)*4.5f,0,(i/4-1.5f)*4.5f)}; AddChild(actor);
                var mesh=new MeshInstance3D {Name="MeshInstance3D",Mesh=new CapsuleMesh {Radius=.45f,Height=2},
                    MaterialOverride=new StandardMaterial3D {AlbedoColor=new(.22f,.72f,.4f)}}; actor.AddChild(mesh);
                var rig=packed.Instantiate<SwordAttackAnimation>(); actor.AddChild(rig); rigs.Add(rig);
                var rest=actor.Transform; var collision=new CollisionShape3D {Shape=new CapsuleShape3D()}; actor.AddChild(collision); var shape=collision.Transform;
                actor.AddChild(new Label3D {Position=new(0,-.8f,1.8f),Text=names[i],FontSize=42,PixelSize=.008f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled});
                if(ids[i]==0)
                {
                    rig.PredictBasic((uint)(i+1),-V2.UnitY,1); rig._Process(.05);
                    var position=rig.ClipPosition;
                    rig.ConfirmBasic(new(new(1),(uint)(i+1),1,V2.Zero,-V2.UnitY,2,new(2),10,90,false));
                    Check(rig.ClipPosition>=position,"Confirmation restarted ordinary wind-up.");
                }
                else
                {
                    rig.PredictAbility(1,ids[i],-V2.UnitY,.5); rig._Process(.1);
                    var position=rig.ClipPosition;
                    rig.ApplyAbility(State(ids[i],AbilityPhase.Telegraph,.4f));
                    Check(rig.ClipPosition==position,"Confirmation restarted skill wind-up.");
                    rig.ApplyAbility(State(ids[i],AbilityPhase.Impact,.15f));
                }
                Check(rig.IsAnimating,"Missing animation.");
                Check(actor.Transform==rest && collision.Transform==shape,"Presentation moved authority/collision.");
                Check(mesh.Transform!=Transform3D.Identity,"Body was not animated.");
                rig.SetProcess(false);
            }
            if(OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/sword-animation-poses.png");
            }
            foreach(var rig in rigs)
            {
                rig._Process(1); Check(!rig.IsAnimating,"Recovery never finished.");
                Check(rig.GetParent().GetNode<Node3D>("MeshInstance3D").Transform==Transform3D.Identity,"Rest pose not restored.");
            }
            var test=rigs[0];
            test.PredictBasic(100,V2.UnitY,1); test.RejectBasic(100); Check(!test.IsAnimating,"Rejected attack stuck.");
            test.PredictAbility(101,23,V2.UnitY,.6); test.RejectAbility(100); Check(test.IsAnimating,"Old rejection cancelled new cast.");
            test.RejectAbility(101); Check(!test.IsAnimating,"Rejected cast stuck.");
            test.PredictAbility(102,23,V2.UnitY,.6);
            test.ApplyAbility(State(20,AbilityPhase.Impact,.15f) with {Sequence=101});
            test.ConfirmBasic(new(new(1),90,1,V2.Zero,V2.UnitY,2,new(2),10,90,false));
            Check(test.Clip=="breaker","Delayed old confirmation replaced the new wind-up."); test.Stop();
            test.PredictAbility(1,28,V2.UnitY,1.2); test.ApplyAbility(State(28,AbilityPhase.Telegraph,1.2f));
            test.ApplyAbility(State(28,AbilityPhase.Finished,0)); Check(!test.IsAnimating,"Interrupted recovery stuck.");
            test.PredictBasic(102,V2.UnitY,1); test._Process(2.1); Check(!test.IsAnimating,"Prediction timeout stuck.");
            var avatar=GD.Load<PackedScene>("res://Scenes/PlayerAvatar.tscn").Instantiate<ProjectG.Gameplay.PlayerController>(); AddChild(avatar);
            avatar.SwordAnimation.PredictBasic(1,V2.UnitY,1); avatar.SetAlive(false); Check(!avatar.SwordAnimation.IsAnimating,"Death animation cleanup missing.");
            avatar.QueueFree(); await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GD.Print("SWORD_ANIMATION_SMOKE_OK: 13 clips, distinct poses, confirmation, rejection, interruption, timeout, death, rest and authority isolation.");
            GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static AbilityEffectState State(ushort id,AbilityPhase phase,float remaining)=>new(1,new(1),1,1,id,
        id==28?AbilityForm.Recovery:id==3?AbilityForm.Dash:AbilityForm.Melee,phase,V2.Zero,V2.Zero,-V2.UnitY,2,0,remaining);
    private static void Check(bool value,string failure) { if(!value)throw new Exception(failure); }
}
