using Content.Shared.Network;
using Godot;
using ProjectG.Combat;
using ProjectG.Gameplay;
using ProjectG.UI;
using V2=System.Numerics.Vector2;
namespace ProjectG.Actors;

/// <summary>Real Godot scene assertions: authority isolation, public timelines, previews and bounded cleanup.</summary>
public partial class CapsuleActorSmoke : Node3D
{
    public override async void _Ready()
    {
        try
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var variants=new[]{"PlayerFigure","ResidentFigure","TrainerFigure","GuideFigure","KeeperFigure","MonsterFigure","BossFigure"};
            foreach(var name in variants)
            {
                var visual=Spawn(name); visual.SetProcess(false);
                Check(visual.GetNode<MeshInstance3D>("Center/Pose/Body").Mesh is CapsuleMesh,name+" is not CapsuleMesh");
                Check(visual.FindChildren("*","Skeleton3D",true,false).Count==0,name+" has a skeleton");
                Check(visual.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>()
                    .Count(m=>m.Mesh is CapsuleMesh)==1,name+" must contain exactly one capsule");
                var root=visual.Transform;
                var player=visual.GetNode<AnimationPlayer>("AnimationPlayer");
                Check(player.GetAnimationLibrary("").ResourcePath.EndsWith("CapsuleAnimations.tres"),"Embedded/duplicated animation library");
                foreach(var clip in visual.ClipNames)
                {
                    var animation=player.GetAnimation(clip);
                    for(var track=0;track<animation.GetTrackCount();track++)
                        Check(animation.TrackGetPath(track).ToString().StartsWith("Center/Pose:" ) ||
                            animation.TrackGetPath(track).ToString().StartsWith("Center/Pose/Weapon:"),"Animation touches authority");
                    foreach(var t in new[]{0,.35,.8,1})
                    {
                        visual.Sample(clip,t*animation.Length);
                        var pose=visual.GetNode<Node3D>("Center/Pose").Transform;
                        Check(pose.Origin.IsFinite() && pose.Basis.X.IsFinite() && pose.Basis.Y.IsFinite() && pose.Basis.Z.IsFinite(),clip+" invalid transform");
                        Check(visual.Transform==root,clip+" changed placement");
                    }
                }
                visual.Sample("dead_back",0);
                var body=visual.GetNode<MeshInstance3D>("Center/Pose/Body");
                var mesh=(CapsuleMesh)body.Mesh;
                Check(body.GlobalPosition.Y-mesh.Radius>=-.002f,name+" death below ground");
                visual.QueueFree();
            }
            await Frame();
            var avatar=GD.Load<PackedScene>("res://Scenes/PlayerAvatar.tscn").Instantiate<PlayerController>(); AddChild(avatar);
            var figure=(CapsuleActorVisual)avatar.Animator; figure.PreviewOnly=true; figure.SetProcess(false);
            var authority=avatar.Transform; var collision=avatar.GetNode<CollisionShape3D>("CollisionShape3D").Transform;
            figure.Apply(State(1,true)); figure._Process(.4);
            avatar.SwordAnimation.PredictBasic(1,V2.UnitY,1); avatar.SwordAnimation._Process(.05); figure._Process(.016);
            Check(figure.CurrentClip.StartsWith("basic"),"Capsule attack was not sampled");
            var hit=new AttackEvent(new(1),1,2,V2.Zero,V2.UnitY,2,new(2),10,90,false,ExecutionId:1);
            avatar.SwordAnimation.ConfirmBasic(hit); avatar.SwordAnimation._Process(.12);
            var progress=avatar.SwordAnimation.ClipPosition; avatar.SwordAnimation.ConfirmBasic(hit);
            Check(avatar.SwordAnimation.ClipPosition==progress,"Duplicate restarted contact");
            avatar.SwordAnimation._Process(1); figure._Process(.2);
            Check(!avatar.SwordAnimation.IsAnimating,"Attack recovery stuck");
            avatar.SwordAnimation.PredictBasic(2,V2.UnitY,1); avatar.SwordAnimation.RejectBasic(2); figure._Process(.2);
            Check(!avatar.SwordAnimation.IsAnimating && !figure.CurrentClip.StartsWith("basic"),"Rejected prediction stuck");
            var aimed=new BasicActionState(new(1),900,3,3,Content.Shared.Combat.CombatActionPhase.Preparation,V2.Zero,V2.UnitY,0,2,Mathf.Pi/4,
                .2,.1,.7,.2,1,true,Content.Shared.Combat.ActionCancelPermission.Parry,OriginHeightOffset:1,Thickness:.3f);
            avatar.SwordAnimation.ApplyBasic(aimed);avatar.SwordAnimation._Process(.1);figure._Process(.2);
            var weapon=figure.GetNode<Node3D>("Center/Pose/Weapon");var flat=weapon.Transform;
            avatar.SwordAnimation.ApplyBasic(aimed with {Direction=new(0,.8f),DirectionY=.6f});
            avatar.SwordAnimation._Process(.1);figure._Process(.2);
            var tilt=new Basis(Vector3.Right,-Mathf.Asin(.6f));
            var expected=new Transform3D(tilt*flat.Basis,tilt*flat.Origin);
            Check(weapon.Transform.IsEqualApprox(expected),"Confirmed 3D pitch did not reach the capsule weapon pose");
            figure._Process(.01);Check(weapon.Transform.IsEqualApprox(expected),"Attack pitch accumulated across render frames");
            avatar.SwordAnimation.Stop();
            avatar.SwordAnimation.ApplyBasic(aimed);avatar.SwordAnimation._Process(.1);figure._Process(.2);
            Check(weapon.Transform.IsEqualApprox(flat),"Previous attack pitch leaked into the next basic");
            avatar.SwordAnimation.Stop();
            avatar.SwordAnimation.PredictAbility(900,22,V2.UnitY,.2);avatar.SwordAnimation._Process(.1);figure._Process(.2);
            var flatSkill=weapon.Transform;avatar.SwordAnimation.Stop();
            avatar.SwordAnimation.PredictAbility(901,22,new(0,.8f),.2,.6f);avatar.SwordAnimation._Process(.1);figure._Process(.2);
            Check(weapon.Transform.IsEqualApprox(new Transform3D(tilt*flatSkill.Basis,tilt*flatSkill.Origin)),"Manual skill pitch did not reach its weapon pose");
            avatar.SwordAnimation.Stop();
            ushort[] skills=[3,20,21,22,23,24,25,26,27,28,29,1,4];
            uint sequence=10;
            foreach(var skill in skills)
            {
                avatar.SwordAnimation.PredictAbility(++sequence,skill,V2.UnitY,.5);
                avatar.SwordAnimation._Process(.1); figure._Process(.02);
                Check(avatar.SwordAnimation.IsAnimating,"Missing skill presentation "+skill);
                avatar.SwordAnimation.RejectAbility(sequence); figure._Process(.2);
            }
            figure.Apply(State(2,true) with {Blocking=true}); figure._Process(.4);
            Check(figure.CurrentClip=="block_hold","Block hold missing");
            figure.Apply(State(3,true) with {ParryRemaining=.2f}); figure._Process(.1);
            Check(figure.CurrentClip=="parry","Parry missing");
            figure.Apply(State(4,true) with {StunRemaining=.3f}); figure._Process(.1);
            Check(figure.CurrentClip=="stun_loop","Stun missing"); figure._Process(.4);
            foreach(var phase in new[]{CombatControlPhase.Airborne,CombatControlPhase.KnockedDown,CombatControlPhase.Recovering})
            {
                figure.SetControl(phase,.3); figure.Attack("basic_left",.35f,0); figure._Process(.03);
                Check(figure.CurrentClip is "airborne" or "down_hold" or "recover","Control lost to attack");
            }
            figure.SetControl(CombatControlPhase.None,0);
            foreach(var gesture in Enum.GetValues<AvatarGesture>().Where(g=>g!=AvatarGesture.None))
            {
                figure.Apply(State(++sequence,true) with {Gesture=gesture,GestureSequence=sequence}); figure._Process(.02);
                Check(figure.CurrentClip==ActorVisual.GestureClip(gesture),"Gesture "+gesture);
            }
            figure.Apply(State(++sequence,true) with {Gesture=AvatarGesture.Sit,GestureSequence=sequence}); figure._Process(1);
            Check(figure.CurrentClip=="sit_idle","Sit transition missing");
            avatar.SetAlive(false); figure._Process(1.3);
            Check(figure.CurrentClip=="dead_back" && !avatar.SwordAnimation.IsAnimating,"Death cleanup");
            avatar.SetAlive(true); figure._Process(.2);
            Check(figure.CurrentClip=="combat_idle","Respawn pose leaked");
            Check(avatar.Transform==authority && avatar.GetNode<CollisionShape3D>("CollisionShape3D").Transform==collision,"Visual changed authority/collision");
            avatar.QueueFree(); await Frame();
            var npc=Spawn("MonsterFigure"); npc.SetProcess(false);
            var timeline=new NpcActorAnimation(); AddChild(timeline); timeline.Bind(npc,100,false);
            var windup=new NpcWindup(new(3),1,101,V2.Zero,V2.UnitY,2,.5f);
            timeline.Windup(windup); timeline._Process(.25); npc._Process(.016);
            Check(timeline.IsAnimating && npc.CurrentClip=="npc_strike","NPC windup");
            timeline.Windup(windup with {RemainingSeconds=0,ServerTick=102}); npc._Process(.2);
            Check(!timeline.IsAnimating && npc.CurrentClip=="idle","NPC cancellation");
            var contact=new AttackEvent(new(3),2,103,V2.Zero,V2.UnitY,2,new(1),10,90,false);
            timeline.Confirm(contact); timeline._Process(.1); timeline.Confirm(contact with {TargetId=new(2)}); timeline._Process(1);
            Check(!timeline.IsAnimating,"AoE duplicate contact stuck");
            timeline.Confirm(contact with {ServerTick=104});
            Check(!timeline.IsAnimating,"Late duplicate replays finished strike");
            timeline.Area(new(new(3),3,105,V2.Zero,3,.5f,NpcAreaPhase.Telegraph));
            timeline._Process(.25); timeline.Area(new(new(3),3,106,V2.Zero,3,.1f,NpcAreaPhase.Impact));
            timeline._Process(.05); npc._Process(.016);
            Check(npc.CurrentClip=="boss_slam","NPC area impact");
            timeline.Facing(new(new(3),106,false,false,V2.UnitX,0,0,AvatarGesture.None,0,0));timeline._Process(.01);npc._Process(.1);
            Check(Mathf.Abs(npc.Rotation.Y-Mathf.Pi/2)<.001,"NPC area ignored authoritative combat orientation");
            var basic=new BasicActionState(new(3),100,4,107,Content.Shared.Combat.CombatActionPhase.Preparation,V2.Zero,V2.UnitY,0,2,1,
                20,.1,.4,20,0,false,Content.Shared.Combat.ActionCancelPermission.None);
            timeline.Basic(basic);timeline._Process(13);
            Check(timeline.IsAnimating && timeline.ClipPosition<.35,"Confirmed slow NPC preparation was timed out");
            timeline.Basic(basic with {Phase=Content.Shared.Combat.CombatActionPhase.Active,RemainingSeconds=.1});
            timeline.Confirm(contact with {Sequence=4,ServerTick=107,ExecutionId=100});timeline._Process(.1);
            Check(timeline.IsAnimating && timeline.ClipPosition>.5,"Confirmed NPC Active lost its duration");
            timeline.Basic(basic with {Phase=Content.Shared.Combat.CombatActionPhase.Recovery,RemainingSeconds=.4});timeline._Process(.2);
            Check(timeline.IsAnimating && timeline.ClipPosition<1,"NPC recovery used the old fixed contact timeout");
            timeline.Basic(basic with {Phase=Content.Shared.Combat.CombatActionPhase.Finished,RemainingSeconds=0});
            Check(!timeline.IsAnimating,"Finished NPC basic retained its pose");
            timeline.SetAlive(false); timeline.Windup(windup with {Sequence=5,ServerTick=108}); npc._Process(1.3);
            Check(!timeline.IsAnimating && npc.CurrentClip=="dead_back","Dead NPC restarted windup");
            timeline.QueueFree(); npc.QueueFree(); await Frame();
            var preview=new UiModelPreview(); AddChild(preview); preview.SetVisual(ActorScenes.Load("TrainerFigure"));
            var port=preview.FindChildren("*","SubViewport",true,false).OfType<SubViewport>().Single();
            Check(port.OwnWorld3D && preview.FindChildren("*","",true,false).OfType<CapsuleActorVisual>().Single().PreviewOnly,"UI preview differs/leaks sound");
            preview.QueueFree(); await Frame();
            var before=GetTree().GetNodeCount();
            for(var pass=0;pass<3;pass++)
            {
                var group=new Node3D(); AddChild(group);
                for(var i=0;i<32;i++)
                {
                    var item=ActorScenes.Load("PlayerFigure").Instantiate<CapsuleActorVisual>(); item.PreviewOnly=true; item.Position=new(i%8*2,0,i/8*2);
                    group.AddChild(item);
                }
                await Frame();
                group.QueueFree(); await Frame(); await Frame();
                Check(GetTree().GetNodeCount()==before,"Repeated spawn/despawn leaked nodes");
            }
            var stuck = Spawn("PlayerFigure"); var stuckPosition = stuck.GlobalPosition;
            stuck.SetMovementIntent(V2.UnitX, 3.5f); stuck._Process(.5);
            Check(stuck.CurrentClip.StartsWith("run"), "Blocked cursor movement must keep the run animation");
            Check(stuck.GlobalPosition == stuckPosition && Mathf.Abs(stuck.Rotation.Y-Mathf.Pi/2)<.001f, "Running in place moved the actor or lost its direction");
            stuck.SetMovementIntent(V2.Zero, 0); stuck._Process(1);
            Check(!stuck.CurrentClip.StartsWith("run"), "Releasing cursor input retained the run animation");
            stuck.QueueFree(); await Frame();
            GD.Print("CAPSULE_ACTOR_SMOKE_OK: 7 variants, 65 native clips, collision isolation, combat/controls/emotes, NPC lifecycle, UI, 3x32 cleanup, blocked cursor run-in-place.");
            GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private CapsuleActorVisual Spawn(string name)
    {
        var actor=ActorScenes.Load(name).Instantiate<CapsuleActorVisual>(); actor.PreviewOnly=true; AddChild(actor); return actor;
    }
    private async Task Frame() { await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
    private static AvatarState State(uint tick,bool armed)=>new(new(1),tick,armed,false,V2.UnitY,0,0,AvatarGesture.None,0,0);
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
}
