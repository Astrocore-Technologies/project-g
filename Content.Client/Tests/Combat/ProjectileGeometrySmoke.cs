using Content.Shared.Navigation;
using Content.Shared.Network;
using Content.Shared.Combat;
using Godot;
using ProjectG.Combat;
using V2=System.Numerics.Vector2;
using V3=System.Numerics.Vector3;

namespace ProjectG.Tests.Combat;

public partial class ProjectileGeometrySmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var grid=new NavigationGrid(new(new(-10,-10),1,.25f,20,20,new byte[400]));
            var area=new AbilityAreaVisual();AddChild(area);
            var profile=new AbilityProfile(1,AbilityForm.Projectile,5,.25f,4,0,1,0,0,
                Area:new(AbilityAreaShape.Corridor,5,.25f),ProjectileLaunchHeight:1);
            area.Show(profile,grid,new(0,2,0),new(4,6,0),new(.8f,0),.6f,V3.Zero,true);
            var mesh=area.GetChildren().OfType<MeshInstance3D>().Single().Mesh;
            var box=mesh.GetAabb();
            if(!area.Visible || area.VertexCount!=6 || Math.Abs(box.Position.Y-3)>.0001 || Math.Abs(box.Size.Y-3)>.0001)
                throw new Exception($"Projectile preview origin/pitch mismatch: {box}");
            var visual=new AbilityEffectVisual();AddChild(visual);
            visual.Apply(new(1,new(1),1,1,1,AbilityForm.Projectile,AbilityPhase.Flying,
                V2.Zero,new(1,0),new(.8f,0),.25f,4,1,3,4,.6f,profile.Area));
            if(!visual.Position.IsEqualApprox(new(1,4,0)))throw new Exception($"Flying visual offset: {visual.Position}");
            var melee=new AbilityProfile(22,AbilityForm.Melee,3,.3f,0,.2,1,0,0,Area:new(AbilityAreaShape.Sector,3,HalfAngleRadians:Mathf.Pi/4),
                MeleeOriginHeight:1,MeleeThickness:.3f);
            area.Show(melee,grid,new(0,2,0),new(4,6,0),new(.8f,0),.6f,V3.Zero,true);
            var meleeBox=area.GetChildren().OfType<MeshInstance3D>().Single().Mesh.GetAabb();
            if(!area.Visible || meleeBox.Position.Y<2.87 || meleeBox.Position.Y>2.89 || meleeBox.End.Y<4.7)
                throw new Exception($"Melee preview remained on the ground: {meleeBox}");
            area.Show(melee,grid,new(0,2,0),new(0,6,0),V2.Zero,1,V3.Zero,true,V2.UnitX);
            meleeBox=area.GetChildren().OfType<MeshInstance3D>().Single().Mesh.GetAabb();
            if(Math.Abs(meleeBox.Size.X-.3f)>.001 || meleeBox.Size.Z<1)throw new Exception("Vertical melee preview lost the server-facing plane or thickness.");
            visual.Apply(new(2,new(1),1,1,22,AbilityForm.Melee,AbilityPhase.Telegraph,V2.Zero,V2.Zero,new(.8f,0),3,0,.2f,
                OriginHeight:3,Height:2,DirectionY:.6f,Area:melee.Area,MeleeThickness:.3f));
            if(!visual.Position.IsEqualApprox(new(0,3,0)) || Math.Abs(visual.Basis.Z.Y-.6f)>.001)
                throw new Exception("Confirmed melee visual lost its origin or pitch.");
            var ground=new AbilityProfile(2,AbilityForm.GroundArea,8,2,0,.5,1,0,0,
                Area:new(AbilityAreaShape.Circle,2),GroundVolumeHeight:2);
            area.Show(ground,grid,V3.Zero,new(2,3,1),V2.UnitY,0,V3.Zero,true);
            var cylinderBox=area.GetChildren().OfType<MeshInstance3D>().Single().Mesh.GetAabb();
            if(!area.Visible || area.VertexCount!=384 || Math.Abs(cylinderBox.Position.Y-3)>.001 || Math.Abs(cylinderBox.End.Y-5)>.001 ||
                Math.Abs(cylinderBox.Size.X-4)>.001 || Math.Abs(cylinderBox.Size.Z-4)>.001)
                throw new Exception($"Ground preview lost the selected floor or cylinder dimensions: {cylinderBox}");
            var groundState=new AbilityEffectState(3,new(1),2,2,2,AbilityForm.GroundArea,AbilityPhase.Channeling,
                V2.Zero,new(2,1),V2.UnitY,2,0,1,Height:3,GroundVolumeHeight:2);
            visual.Apply(groundState);
            if(!visual.Position.IsEqualApprox(new(2,4,1)) || visual.GetChildren().OfType<MeshInstance3D>().Single().Mesh is not CylinderMesh {Height:2})
                throw new Exception("Confirmed GroundArea did not use the public cylinder height.");
            visual.Apply(groundState with {Height=4,ServerTick=3});
            if(!visual.Position.IsEqualApprox(new(2,5,1)))throw new Exception("Repeated GroundArea pose lost its cylinder centre.");
            var root=new Node3D{Position=new(0,1,0)};AddChild(root);
            var body=new CombatAimBody();root.AddChild(body);body.Configure(new(CombatBodyShape.Capsule,2,.4f));
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Godot.Collections.Dictionary Ray(float y,float z=0)
            {
                var query=PhysicsRayQueryParameters3D.Create(new(-2,y,z),new(2,y,z),CombatAimBody.Layer);
                query.CollideWithAreas=true;query.CollideWithBodies=false;
                return body.GetWorld3D().DirectSpaceState.IntersectRay(query);
            }
            if(Ray(1).Count==0)throw new Exception("Standing 3D aim body was not picked.");
            body.Pose(CombatControlPhase.KnockedDown,V2.UnitY);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            if(Ray(1).Count!=0 || Ray(.4f,.9f).Count==0)throw new Exception("Prone aim body did not change height/length.");
            root.Position=new(0,3,0);body.Pose(CombatControlPhase.Airborne,V2.UnitY);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            if(Ray(1).Count!=0 || Ray(3).Count==0)throw new Exception("Airborne aim body left a grounded proxy.");
            body.Alive(false);await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            if(Ray(3).Count!=0)throw new Exception("Dead aim body still intercepted the cursor.");
            using var network=new ProjectG.Networking.NetworkClient();
            var rangedActor=new Node3D();AddChild(rangedActor);var combat=new CombatPresentation();rangedActor.AddChild(combat);
            combat.Initialize(new(new(51),1,CombatEntityKind.Player,V2.Zero,100,100,1.2,10,0,
                BasicOriginHeight:1,BasicDelivery:BasicAttackDelivery.Projectile,BasicProjectileSpeed:20,BasicProjectileRadius:.1f),network,false);
            var preparation=new BasicActionState(new(51),501,1,2,CombatActionPhase.Preparation,V2.Zero,V2.UnitY,0,10,0,
                .36,.12,.72,.36,1,true,ActionCancelPermission.Voluntary,OriginHeightOffset:1,Delivery:BasicAttackDelivery.Projectile);
            combat.ApplyBasicAction(preparation);
            if(!combat.GetChildren().OfType<Label3D>().Any(l=>l.Text.Contains("Подготовка выстрела")))throw new Exception("Ranged preparation lost its visible phase.");
            combat.Confirm(new(new(51),1,3,V2.Zero,V2.UnitY,10,new(52),10,90,false,ExecutionId:501,OriginHeight:1,Delivery:BasicAttackDelivery.Projectile));
            if(combat.GetChildren().OfType<MeshInstance3D>().Any(m=>m.Visible))throw new Exception("A confirmed ranged contact drew a melee slash.");
            combat.ApplyBasicAction(preparation with {Phase=CombatActionPhase.Finished,RemainingSeconds=0,ServerTick=4});
            if(combat.GetChildren().OfType<Label3D>().Any(l=>l.Text.Contains("Подготовка выстрела")))throw new Exception("Finished ranged preparation remained visible.");
            var basicVisual=new AbilityEffectVisual();AddChild(basicVisual);
            basicVisual.Apply(new(501,new(51),1,3,0,AbilityForm.Projectile,AbilityPhase.Flying,V2.Zero,V2.Zero,V2.UnitY,.1f,20,.4f,1,1));
            basicVisual._Process(.1);if(basicVisual.Position.Z<1.99f || Math.Abs(basicVisual.Position.Y-1)>.001)throw new Exception("Basic projectile presentation lost its confirmed height or travel.");
            GD.Print("PROJECTILE_GEOMETRY_SMOKE_OK");GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}
