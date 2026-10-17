using Godot;
namespace ProjectG.Animation;

/// <summary>Editable actor library inspection and rendered/headless deformation smoke test.</summary>
public partial class CharacterAnimationGallery : Node3D
{
    private readonly List<(CharacterAnimator Actor,string Clip)> _actors=new();
    private double _time;
    private int _page;
    private static readonly string[][] Pages = [
        ["idle","walk","run","combat_idle","walk_sword","run_sword","block_hold","parry_success","dash","hit_front","death_back","cast_hold"],
        ["basic_left","basic_right","thrust","sweep","rend","breaker","pommel","hamstring","riposte","whirl","breath","finisher"],
        ["wave","nod","no","bow","point","cheer","clap","sit_idle","pickup","gather","craft","talk"]];
    public override async void _Ready()
    {
        try
        {
            GetWindow().Size=new(1500,1000);
            AddChild(new WorldEnvironment { Environment=new Godot.Environment { BackgroundMode=Godot.Environment.BGMode.Color,
                BackgroundColor=new("263540"),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Colors.White,AmbientLightEnergy=.65f } });
            AddChild(new DirectionalLight3D {RotationDegrees=new(-45,-25,0),LightEnergy=1.3f,ShadowEnabled=true});
            var camera=new Camera3D {Position=new(0,10,16),Projection=Camera3D.ProjectionType.Orthogonal,Size=10.5f,Current=true}; AddChild(camera);camera.LookAt(new(0,0,0));
            AddChild(new MeshInstance3D { Mesh=new PlaneMesh {Size=new(20,20)},Position=new(0,-.015f,0),MaterialOverride=new StandardMaterial3D {AlbedoColor=new("52646a")} });
            var layer=new CanvasLayer();AddChild(layer);var bar=new HBoxContainer {Position=new(18,18)};layer.AddChild(bar);
            for(var i=0;i<3;i++) {var page=i;var button=new Button {Text=new[]{"Движения и защита","Все приёмы мечника","Жесты и взаимодействия"}[i]};bar.AddChild(button);button.Pressed+=()=>ShowPage(page);}
            ShowPage(0);
            if(!OS.GetCmdlineUserArgs().Contains("--smoke"))return;
            var probe=_actors[0].Actor;
            if(probe.ClipNames.Count<64)throw new Exception($"Only {probe.ClipNames.Count} imported clips.");
            var origin=probe.Transform;
            foreach(var name in probe.ClipNames)
                foreach(var phase in new[]{0,.2,.35,.7,1.0})
                {
                    probe.Sample(name,phase);
                    for(var bone=0;bone<probe.Skeleton.GetBoneCount();bone++)
                        if(!probe.Skeleton.GetBoneGlobalPose(bone).Origin.IsFinite())throw new Exception($"Invalid pose: {name}");
                    if(probe.Transform!=origin)throw new Exception("Animation moved authoritative scene root.");
                }
            GD.Print($"AVATAR_LIBRARY_OK clips={probe.ClipNames.Count} bones={probe.Skeleton.GetBoneCount()}");
            for(ushort id=20;id<=29;id++)
            {
                if(SkillEffectStyle.Get(id) is null)throw new Exception($"Missing effect style {id}");
                var effect=new ProjectG.Combat.AbilityEffectVisual();AddChild(effect);
                var state=new Content.Shared.Network.AbilityEffectState(id,new(1),1,1,id,
                    id==28 ? Content.Shared.Network.AbilityForm.Recovery : Content.Shared.Network.AbilityForm.Melee,
                    Content.Shared.Network.AbilityPhase.Telegraph,System.Numerics.Vector2.Zero,System.Numerics.Vector2.Zero,System.Numerics.Vector2.UnitY,2,0,.1f);
                effect.Apply(state);effect.Apply(state with { Phase=Content.Shared.Network.AbilityPhase.Impact });
                effect._Process(1);if(!effect.IsQueuedForDeletion())throw new Exception("Impact TTL did not clean up.");
            }
            var sounds=0;
            foreach(var file in DirAccess.GetFilesAt("res://Assets/Audio/Character"))if(file.EndsWith(".wav"))
            { if(GD.Load<AudioStream>("res://Assets/Audio/Character/"+file).GetLength()<=0)throw new Exception("Invalid sound: "+file);sounds++; }
            if(sounds!=23)throw new Exception($"Expected 23 sounds, found {sounds}");
            GD.Print("AVATAR_EFFECTS_OK: ten skill styles, decoded audio, bounded effect lifetime.");
            if(OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                for(var page=0;page<3;page++)
                {
                    ShowPage(page);_time=.35;SetProcess(false);SampleAll(.35);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng($"res://../.artifacts/animation-gallery-{page}.png");
                }
            }
            GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private void ShowPage(int page)
    {
        foreach(var actor in _actors) {RemoveChild(actor.Actor);actor.Actor.QueueFree();}
        _actors.Clear();_page=page;_time=0;
        var scene=GD.Load<PackedScene>("res://Assets/Characters/MaleBase/MaleCharacter.tscn");
        for(var i=0;i<Pages[page].Length;i++)
        {
            var actor=scene.Instantiate<CharacterAnimator>();actor.Position=new((i%4-1.5f)*2.35f,0,(i/4-1)*3.2f);
            actor.FaceVariant=i%3+1;actor.HairVariant=i/3%3+1;actor.PreviewArmed=page<2;AddChild(actor);
            actor.SetProcess(false);
            foreach(var n in actor.FindChildren("Sword","MeshInstance3D",true,false))((MeshInstance3D)n).Visible=page<2;
            var clip=Pages[page][i]; actor.AddChild(new Label3D {Position=new(0,2.15f,0),Text=clip,FontSize=32,PixelSize=.0035f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled});
            _actors.Add((actor,clip));
        }
    }
    private void SampleAll(double time) { foreach(var (actor,clip) in _actors) actor.Sample(clip,time); }
    public override void _Process(double delta) { _time+=delta;SampleAll(_time%2); }
}
